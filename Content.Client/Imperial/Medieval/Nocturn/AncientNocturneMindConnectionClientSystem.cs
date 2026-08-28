using Content.Client.UserInterface.Systems.Chat;
using Content.Shared.Chat;
using Content.Shared.Nocturn.Components;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client.Imperial.Medieval.Nocturn;

public sealed class AncientNocturneMindConnectionClientSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<AncientNocturneConversionNotificationEvent>(OnConversionNotification);
    }

    private void OnConversionNotification(AncientNocturneConversionNotificationEvent args)
    {
        var messageId = args.Type switch
        {
            AncientNocturneConversionNotification.FirstTrall =>
                "medieval-ancient-nocturne-mind-connection-first-trall",
            AncientNocturneConversionNotification.Converted =>
                "medieval-ancient-nocturne-mind-connection-converted",
            _ => throw new ArgumentOutOfRangeException()
        };

        var key = Loc.GetString("medieval-ancient-nocturne-mind-connection-chat-key");
        var message = Loc.GetString(messageId, ("key", key));
        var chatMessage = new ChatMessage(
            ChatChannel.Server,
            message,
            FormattedMessage.EscapeText(message),
            default,
            null,
            colorOverride: Color.Yellow);

        _ui.GetUIController<ChatUIController>().ProcessChatMessage(chatMessage, false);
    }
}
