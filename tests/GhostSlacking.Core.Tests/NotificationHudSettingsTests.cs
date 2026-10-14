using GhostSlacking.Core;

namespace GhostSlacking.Core.Tests;

public sealed class NotificationHudSettingsTests
{
    [Fact]
    public void Legacy_styles_receive_the_same_hud_defaults_and_preserve_other_preferences()
    {
        foreach (var style in new[] { "Card", "Capsule", "Chat" })
        {
            var settings = AppSettingsJson.Deserialize("{\"SchemaVersion\":7,\"NotificationStyle\":\"" + style + "\",\"Language\":\"English\",\"RevealDiameterPx\":320}");
            Assert.Equal(8, settings.SchemaVersion);
            Assert.Equal(new NotificationHudSettings(), settings.NotificationHud);
            Assert.Equal(UiLanguage.English, settings.Language);
            Assert.Equal(320, settings.RevealDiameterPx);
            Assert.Equal(style, settings.NotificationStyle.ToString());
        }
    }

    [Fact]
    public void All_five_hud_preferences_round_trip_with_legacy_fields()
    {
        var settings = new AppSettings
        {
            NotificationStyle = NotificationStyle.Chat,
            ChatNotificationPlacement = new NotificationPlacement(.2, .3),
            NotificationHud = new NotificationHudSettings
            {
                CornerRadius = 16,
                Width = 180,
                Height = 52,
                HorizontalPosition = 20,
                VerticalPosition = 30
            }
        };
        Assert.Equal(settings.Normalize(), AppSettingsJson.Deserialize(AppSettingsJson.Serialize(settings)));
    }

    [Fact]
    public void Invalid_values_are_finite_and_bounded_before_they_reach_the_renderer()
    {
        var normalized = new NotificationHudSettings
        {
            CornerRadius = double.NaN,
            Width = double.PositiveInfinity,
            Height = -10,
            HorizontalPosition = 500,
            VerticalPosition = double.NaN
        }.Normalize();
        Assert.Equal(10, normalized.CornerRadius);
        Assert.Equal(132, normalized.Width);
        Assert.Equal(32, normalized.Height);
        Assert.Equal(100, normalized.HorizontalPosition);
        Assert.Equal(100, normalized.VerticalPosition);
        Assert.Equal(new NotificationHudSettings(), AppSettingsJson.Deserialize("{\"NotificationHud\":null}").NotificationHud);
    }
}
