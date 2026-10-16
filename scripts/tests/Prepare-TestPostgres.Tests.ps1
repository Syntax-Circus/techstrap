BeforeAll {
    $script:Prepare = Join-Path $PSScriptRoot '..' 'Prepare-TestPostgres.sh'
    $script:Bash = if ($IsWindows) { Join-Path $env:ProgramFiles 'Git/bin/bash.exe' } else { 'bash' }

    function Invoke-ImagePreparation([string]$Mode) {
        $bin = Join-Path $TestDrive $Mode
        New-Item -ItemType Directory -Path $bin -Force | Out-Null
        $docker = @'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$DOCKER_TEST_LOG"
case "$1" in
  image)
    if [[ "$DOCKER_TEST_MODE" == cached || -f "$DOCKER_TEST_LOG.tagged" ]]; then echo sha256:test; exit 0; fi
    exit 1 ;;
  pull)
    case "$DOCKER_TEST_MODE:$2" in
      primary:docker.io/*|tag-fails:docker.io/*) exit 0 ;;
      fallback:mirror.gcr.io/*|timeout:mirror.gcr.io/*) exit 0 ;;
      third:public.ecr.aws/*) exit 0 ;;
      *) echo 'toomanyrequests: Data limit exceeded' >&2; exit 1 ;;
    esac ;;
  tag)
    [[ "$DOCKER_TEST_MODE" != tag-fails ]] || exit 2
    touch "$DOCKER_TEST_LOG.tagged" ;;
  *) exit 99 ;;
esac
'@
        $timeout = @'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$DOCKER_TEST_LOG.timeouts"
[[ "$1" == --kill-after=5s && "$2" == 60s ]] || exit 99
shift 2
if [[ "$DOCKER_TEST_MODE" == timeout && "$3" == docker.io/* ]]; then exit 124; fi
exec "$@"
'@
        [IO.File]::WriteAllText((Join-Path $bin 'docker'), $docker.Replace("`r`n", "`n") + "`n")
        [IO.File]::WriteAllText((Join-Path $bin 'timeout'), $timeout.Replace("`r`n", "`n") + "`n")
        $log = Join-Path $bin 'commands.log'
        $runner = @'
set -euo pipefail
bin="$1"; prepare="$2"; log="$3"
if command -v cygpath >/dev/null 2>&1; then
  bin="$(cygpath -u "$bin")"; prepare="$(cygpath -u "$prepare")"; log="$(cygpath -u "$log")"
fi
chmod +x "$bin/docker" "$bin/timeout"
export PATH="$bin:$PATH" DOCKER_TEST_LOG="$log" DOCKER_TEST_MODE="$4"
export TECHSTRAP_TEST_POSTGRES_IMAGE=techstrap-test-postgres:17
bash "$prepare"
'@
        $output = & $script:Bash -c $runner -- $bin $script:Prepare $log $Mode 2>&1 | Out-String
        $code = $LASTEXITCODE
        [pscustomobject]@{
            Code = $code
            Output = $output
            Commands = if (Test-Path $log) { Get-Content $log } else { @() }
            Timeouts = if (Test-Path "$log.timeouts") { Get-Content "$log.timeouts" } else { @() }
        }
    }
}

Describe 'Prepare-TestPostgres registry fallback' {
    It 'tags the primary image locally and does not try other registries' {
        $result = Invoke-ImagePreparation primary
        $result.Code | Should -Be 0 -Because $result.Output
        @($result.Commands | Where-Object { $_ -like 'pull *' }) | Should -Be @('pull docker.io/library/postgres:17')
        $result.Commands | Should -Contain 'tag docker.io/library/postgres:17 techstrap-test-postgres:17'
    }

    It 'falls back after the primary registry rejects a pull' {
        $result = Invoke-ImagePreparation fallback
        $result.Code | Should -Be 0 -Because $result.Output
        @($result.Commands | Where-Object { $_ -like 'pull *' }) | Should -Be @('pull docker.io/library/postgres:17', 'pull mirror.gcr.io/library/postgres:17')
        $result.Commands | Should -Contain 'tag mirror.gcr.io/library/postgres:17 techstrap-test-postgres:17'
    }

    It 'tries ECR only after the first two sources fail' {
        $result = Invoke-ImagePreparation third
        $result.Code | Should -Be 0 -Because $result.Output
        @($result.Commands | Where-Object { $_ -like 'pull *' }) | Should -Be @('pull docker.io/library/postgres:17', 'pull mirror.gcr.io/library/postgres:17', 'pull public.ecr.aws/docker/library/postgres:17')
    }

    It 'bounds each pull and falls back after a timeout' {
        $result = Invoke-ImagePreparation timeout
        $result.Code | Should -Be 0 -Because $result.Output
        $result.Timeouts | Should -Contain '--kill-after=5s 60s docker pull docker.io/library/postgres:17'
        $result.Commands | Should -Contain 'tag mirror.gcr.io/library/postgres:17 techstrap-test-postgres:17'
    }

    It 'fails once with a useful error if every registry is unavailable' {
        $result = Invoke-ImagePreparation unavailable
        $result.Code | Should -Be 1
        $result.Output | Should -Match 'Unable to prepare test Postgres image'
        @($result.Commands | Where-Object { $_ -like 'pull *' }).Count | Should -Be 3
        @($result.Commands | Where-Object { $_ -like 'tag *' }).Count | Should -Be 0
    }

    It 'reuses a prepared local alias without any pull' {
        $result = Invoke-ImagePreparation cached
        $result.Code | Should -Be 0 -Because $result.Output
        @($result.Commands | Where-Object { $_ -like 'pull *' }).Count | Should -Be 0
    }

    It 'does not hide a local tagging failure by switching registries' {
        $result = Invoke-ImagePreparation tag-fails
        $result.Code | Should -Be 2
        @($result.Commands | Where-Object { $_ -like 'pull *' }).Count | Should -Be 1
    }
}
