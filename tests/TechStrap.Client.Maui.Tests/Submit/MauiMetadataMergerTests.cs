using System.Text.Json;
using SyntaxCircus.Common;
using TechStrap.Contracts.Intake;

namespace TechStrap.Client.Maui.Tests.Submit;

public sealed class MauiMetadataMergerTests
{
    private static readonly IReadOnlyDictionary<string, string> NoCollected = new Dictionary<string, string>();

    [Fact]
    public void Collected_and_app_keys_are_combined()
    {
        var collected = new Dictionary<string, string> { [TicketMetadataKeys.AppName] = "Puppies Plus" };
        var app = new Dictionary<string, string> { ["screen"] = "checkout" };

        var result = MauiMetadataMerger.Merge(collected, app);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value[TicketMetadataKeys.AppName].ShouldBe("Puppies Plus");
        result.Value["screen"].ShouldBe("checkout");
    }

    [Fact]
    public void An_app_value_for_a_reserved_key_is_ignored()
    {
        var collected = new Dictionary<string, string> { [TicketMetadataKeys.DeviceModel] = "Pixel 8" };
        var app = new Dictionary<string, string>
        {
            [TicketMetadataKeys.DeviceModel] = "spoof",
            [TicketMetadataKeys.BatteryLevel] = "99",
            ["screen"] = "checkout",
        };

        var result = MauiMetadataMerger.Merge(collected, app);

        result.IsSuccess.ShouldBeTrue();
        result.Value[TicketMetadataKeys.DeviceModel].ShouldBe("Pixel 8");
        result.Value.ContainsKey(TicketMetadataKeys.BatteryLevel).ShouldBeFalse();
        result.Value.Count.ShouldBe(2);
    }

    [Fact]
    public void App_metadata_is_truncated_and_blank_values_are_dropped()
    {
        var app = new Dictionary<string, string>
        {
            ["long"] = new string('x', 1_500),
            ["blank"] = "  ",
            ["kept"] = " v ",
        };

        var result = MauiMetadataMerger.Merge(NoCollected, app);

        result.IsSuccess.ShouldBeTrue();
        result.Value["long"].Length.ShouldBe(IntakeLimits.MaxMetadataValueLength);
        result.Value.ContainsKey("blank").ShouldBeFalse();
        result.Value["kept"].ShouldBe("v");
    }

    [Fact]
    public void A_collected_key_over_the_limit_fails_locally() =>
        AssertInvalid(MauiMetadataMerger.Merge(new Dictionary<string, string> { [new string('k', IntakeLimits.MaxMetadataKeyLength + 1)] = "v" }, null));

    [Fact]
    public void A_collected_value_is_truncated_and_a_blank_one_dropped()
    {
        var collected = new Dictionary<string, string>
        {
            [TicketMetadataKeys.DeviceModel] = new string('m', 1_500),
            [TicketMetadataKeys.DeviceManufacturer] = "  ",
        };

        var result = MauiMetadataMerger.Merge(collected, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value[TicketMetadataKeys.DeviceModel].Length.ShouldBe(IntakeLimits.MaxMetadataValueLength);
        result.Value.ContainsKey(TicketMetadataKeys.DeviceManufacturer).ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_key_fails_locally(string key) => AssertInvalid(MauiMetadataMerger.Merge(NoCollected, new Dictionary<string, string> { [key] = "v" }));

    [Fact]
    public void A_key_longer_than_the_limit_fails_locally() =>
        AssertInvalid(MauiMetadataMerger.Merge(NoCollected, new Dictionary<string, string> { [new string('k', IntakeLimits.MaxMetadataKeyLength + 1)] = "v" }));

    [Fact]
    public void A_key_at_the_limit_is_accepted() =>
        MauiMetadataMerger.Merge(NoCollected, new Dictionary<string, string> { [new string('k', IntakeLimits.MaxMetadataKeyLength)] = "v" }).IsSuccess.ShouldBeTrue();

    [Fact]
    public void More_than_the_key_limit_fails_locally()
    {
        var app = Enumerable.Range(0, IntakeLimits.MaxMetadataKeys + 1).ToDictionary(i => $"k{i}", _ => "v");

        AssertInvalid(MauiMetadataMerger.Merge(NoCollected, app));
    }

    [Fact]
    public void Exactly_the_key_limit_is_accepted()
    {
        var app = Enumerable.Range(0, IntakeLimits.MaxMetadataKeys).ToDictionary(i => $"k{i}", _ => "v");

        MauiMetadataMerger.Merge(NoCollected, app).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_serialized_size_boundary_is_exact()
    {
        // The last value holds characters the default encoder escapes (six characters each), so a serializer with a different encoder measures a different length.
        var atLimit = BoundaryDictionary(IntakeLimits.MaxMetadataJsonLength);
        Serialize(atLimit).Length.ShouldBe(IntakeLimits.MaxMetadataJsonLength);
        MauiMetadataMerger.Merge(NoCollected, atLimit).IsSuccess.ShouldBeTrue();

        var overLimit = BoundaryDictionary(IntakeLimits.MaxMetadataJsonLength + 1);
        Serialize(overLimit).Length.ShouldBe(IntakeLimits.MaxMetadataJsonLength + 1);
        AssertInvalid(MauiMetadataMerger.Merge(NoCollected, overLimit));
    }

    [Fact]
    public void An_oversized_dictionary_fails_locally()
    {
        var app = Enumerable.Range(0, 20).ToDictionary(i => $"k{i}", _ => new string('v', IntakeLimits.MaxMetadataValueLength));

        AssertInvalid(MauiMetadataMerger.Merge(NoCollected, app));
    }

    [Fact]
    public void Null_app_metadata_yields_the_collected_set()
    {
        var collected = new Dictionary<string, string> { [TicketMetadataKeys.AppName] = "Puppies Plus" };

        var result = MauiMetadataMerger.Merge(collected, null);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(collected);
    }

    [Fact]
    public void The_failure_message_is_fixed_and_carries_no_value()
    {
        var app = Enumerable.Range(0, IntakeLimits.MaxMetadataKeys + 1).ToDictionary(i => $"k{i}", _ => "secret-value");

        var error = MauiMetadataMerger.Merge(NoCollected, app).Errors[0];

        error.Message.ShouldBe("The metadata exceeds the server's limits (at most 50 keys of 64 characters, values of 1000 characters, 16000 characters in all).");
        error.Message.ShouldNotContain("secret-value");
    }

    private static void AssertInvalid(Result<IReadOnlyDictionary<string, string>> result)
    {
        result.IsSuccess.ShouldBeFalse();
        result.Errors.Count.ShouldBe(1);
        result.Errors[0].ShouldBe(new ResultError(TechStrapMauiErrorCodes.MetadataInvalid, TechStrapMauiMessages.MetadataInvalid, ResultErrorKind.Validation, "metadata"));
    }

    private static string Serialize(Dictionary<string, string> dictionary) => JsonSerializer.Serialize(dictionary, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static Dictionary<string, string> BoundaryDictionary(int targetLength)
    {
        var dictionary = new Dictionary<string, string>();
        for (var i = 0; i < 15; i++)
        {
            dictionary[$"k{i:00}"] = new string('a', IntakeLimits.MaxMetadataValueLength);
        }

        // The last value: '<' serializes to six characters; pad with plain characters to land on the target.
        dictionary["last"] = "<<<<<";
        var missing = targetLength - Serialize(dictionary).Length;
        dictionary["last"] += new string('b', missing);
        return dictionary;
    }
}
