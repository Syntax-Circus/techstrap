namespace TechStrap.AppHost;

/// <summary>
/// Every fixed host port of the local stack (the 552xx block in the design's port table). The AppHost's own launch
/// profile repeats some of these; a test keeps them in step.
/// </summary>
public static class LocalStackPorts
{
    public const int Dashboard = 55200;
    public const int DashboardOtlp = 55201;
    public const int ResourceService = 55202;

    public const int Postgres = 55210;

    public const int MailpitSmtp = 55220;
    public const int MailpitUi = 55221;

    public const int ApiHttps = 55230;
    public const int ApiHttp = 55231;
    public const int AdminHttps = 55240;
    public const int AdminHttp = 55241;
    public const int PortalHttps = 55250;
    public const int PortalHttp = 55251;
    public const int Worker = 55260;
    public const int WebhookCatcher = 55270;

    public const int Authentik = 55290;
}
