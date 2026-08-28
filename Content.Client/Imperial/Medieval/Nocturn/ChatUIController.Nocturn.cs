using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared.Nocturn.Components;

namespace Content.Client.UserInterface.Systems.Chat;

public sealed partial class ChatUIController
{
    private partial bool TryUpdateCustomSelectedChannel(ChatBox box, string text)
    {
        if (_player.LocalEntity is not { Valid: true } entity ||
            !_ent.TryGetComponent<AncientNocturneMindChatComponent>(entity, out var chat))
            return false;

        text = text.TrimStart();
        if (!text.StartsWith(chat.ChatPrefix, StringComparison.OrdinalIgnoreCase) &&
            !text.StartsWith(chat.AlternateChatPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        box.ChatInput.ChannelSelector.Text = Loc.GetString("medieval-ancient-nocturne-mind-connection-channel-name");
        box.ChatInput.ChannelSelector.Modulate = chat.ChatColor;
        return true;
    }
}
