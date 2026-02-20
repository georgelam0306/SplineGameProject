using Derp.Doc.Chat;
using Derp.Doc.Editor;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Widgets;
using FontAwesome.Sharp;

namespace Derp.Doc.Panels;

internal static class ChatPanel
{
    private const float BaseStatusBarHeight = 24f;
    private const float BaseInputAreaHeight = 124f;
    private const float Padding = 6f;
    private const float MessageGap = 6f;
    private const float InputSectionGap = 6f;
    private const float ComposerTopRowGap = 8f;
    private const string ChatInputWidgetId = "chat_panel_input";
    private const string ProviderMenuId = "chat_provider_selector_menu";
    private const string AgentTypeMenuId = "chat_agent_type_selector_menu";
    private const string InputPlaceholderText = "Describe what to build next";
    private static readonly string SendIcon = ((char)IconChar.PaperPlane).ToString();
    private static readonly string AttachmentIcon = ((char)IconChar.Paperclip).ToString();
    private static readonly string ChevronDownIcon = ((char)IconChar.ChevronDown).ToString();
    private static readonly string PlusIcon = ((char)IconChar.Plus).ToString();

    private static readonly char[] InputBuffer = new char[8192];
    private static int _inputLength;
    private static float _messageScrollY;
    private static readonly string[] _suggestions = new string[24];
    private static int _suggestionCount;
    private static int _selectedSuggestionIndex;
    private static readonly List<MessageRenderCache> _messageCaches = new();
    private static readonly string[] ProviderOptions = { "claude", "codex" };
    private static readonly string[] AgentTypeOptions = { "mcp", "workspace" };

    private sealed class MessageRenderCache
    {
        public readonly string WidgetId;
        public char[] Buffer = new char[512];
        public int Length;

        public MessageRenderCache(int messageIndex)
        {
            WidgetId = "chat_msg_body_" + messageIndex;
        }
    }

    public static void Draw(DocWorkspace workspace, ImRect contentRect)
    {
        if (contentRect.Width <= 0f || contentRect.Height <= 0f)
        {
            return;
        }

        ChatSession session = workspace.ChatSession;
        var style = Im.Style;
        float messageFontSize = workspace.UserPreferences.ChatMessageFontSize;
        float inputFontSize = workspace.UserPreferences.ChatInputFontSize;
        float statusBarHeight = MathF.Max(BaseStatusBarHeight, messageFontSize + 10f);
        float inputAreaHeight = MathF.Max(BaseInputAreaHeight, (inputFontSize + 6f) * 3f + 12f);

        uint panelBackground = ImStyle.Lerp(style.Background, 0xFF000000, 0.24f);
        Im.DrawRect(contentRect.X, contentRect.Y, contentRect.Width, contentRect.Height, panelBackground);

        var statusRect = new ImRect(contentRect.X, contentRect.Y, contentRect.Width, statusBarHeight);
        var messageRect = new ImRect(
            contentRect.X,
            contentRect.Y + statusBarHeight,
            contentRect.Width,
            MathF.Max(0f, contentRect.Height - statusBarHeight - inputAreaHeight));
        var inputRect = new ImRect(
            contentRect.X,
            messageRect.Y + messageRect.Height,
            contentRect.Width,
            inputAreaHeight);

        DrawStatusBar(workspace, session, statusRect, messageFontSize);
        DrawMessages(session, messageRect, messageFontSize);
        DrawInputArea(workspace, session, inputRect, inputFontSize);
    }

    private static void DrawStatusBar(DocWorkspace workspace, ChatSession session, ImRect statusRect, float fontSize)
    {
        var style = Im.Style;
        Im.DrawLine(statusRect.X, statusRect.Bottom, statusRect.Right, statusRect.Bottom, 1f, style.Border);

        string statusText;
        uint statusColor = style.TextSecondary;

        if (workspace.IsDirty)
        {
            statusText = "Chat disabled while unsaved local changes exist.";
            statusColor = style.TextPrimary;
        }
        else if (!string.IsNullOrWhiteSpace(session.ErrorMessage))
        {
            statusText = session.ErrorMessage;
            statusColor = style.TextPrimary;
        }
        else if (session.IsProcessing)
        {
            statusText = "Thinking...";
        }
        else if (session.IsConnected)
        {
            string agentTypeText = session.AgentType == ChatAgentType.Workspace ? "Workspace Agent" : "MCP Agent";
            if (string.IsNullOrWhiteSpace(session.CurrentModel))
            {
                statusText = "Connected · " + session.ProviderKind + " · " + agentTypeText;
            }
            else
            {
                statusText = "Connected · " + session.ProviderKind + " · " + agentTypeText + " · " + session.CurrentModel;
            }
        }
        else
        {
            string agentTypeText = session.AgentType == ChatAgentType.Workspace ? "Workspace Agent" : "MCP Agent";
            statusText = "Disconnected · " + session.ProviderKind + " · " + agentTypeText;
        }

        float textY = statusRect.Y + (statusRect.Height - fontSize) * 0.5f;
        Im.Text(statusText.AsSpan(), statusRect.X + Padding, textY, fontSize, statusColor);
    }

    private static void DrawMessages(ChatSession session, ImRect messageRect, float messageFontSize)
    {
        if (messageRect.Width <= 0f || messageRect.Height <= 0f)
        {
            return;
        }

        float bubbleWidth = MathF.Max(48f, messageRect.Width - (Padding * 2f) - Im.Style.ScrollbarWidth - 2f);
        EnsureMessageCacheCount(session.Messages.Count);
        float totalContentHeight = MeasureMessagesHeight(session, bubbleWidth, messageFontSize);

        if (session.RequestScrollToBottom)
        {
            _messageScrollY = MathF.Max(0f, totalContentHeight - messageRect.Height);
            session.RequestScrollToBottom = false;
        }

        float contentOriginY = ImScrollView.Begin(messageRect, totalContentHeight, ref _messageScrollY, handleMouseWheel: true);
        float drawY = contentOriginY + Padding;
        for (int messageIndex = 0; messageIndex < session.Messages.Count; messageIndex++)
        {
            ChatMessage message = session.Messages[messageIndex];
            drawY += DrawMessageBubble(messageIndex, message, messageRect.X + Padding, drawY, bubbleWidth, messageFontSize);
            drawY += MessageGap;
        }
        ImScrollView.End(
            "chat_messages_scroll".GetHashCode(),
            new ImRect(messageRect.Right - Im.Style.ScrollbarWidth, messageRect.Y, Im.Style.ScrollbarWidth, messageRect.Height),
            messageRect.Height,
            totalContentHeight,
            ref _messageScrollY);
    }

    private static float MeasureMessagesHeight(ChatSession session, float bubbleWidth, float messageFontSize)
    {
        float totalHeight = Padding;
        for (int messageIndex = 0; messageIndex < session.Messages.Count; messageIndex++)
        {
            totalHeight += MeasureMessageHeight(session.Messages[messageIndex], bubbleWidth, messageFontSize);
            totalHeight += MessageGap;
        }

        totalHeight += Padding;
        return totalHeight;
    }

    private static float MeasureMessageHeight(ChatMessage message, float bubbleWidth, float messageFontSize)
    {
        float lineHeight = messageFontSize + 4f;
        float bodyLines = 0f;
        float bodyWidth = MathF.Max(24f, bubbleWidth - (Padding * 2f) - 2f);
        bool isToolMessage = message.Role == ChatRole.ToolUse || message.Role == ChatRole.ToolResult;
        if (isToolMessage)
        {
            if (message.IsExpanded)
            {
                string payload = GetMessageBody(message);
                bodyLines = CountWrappedLines(payload, bodyWidth, messageFontSize);
            }
        }
        else
        {
            string payload = GetMessageBody(message);
            bodyLines = CountWrappedLines(payload, bodyWidth, messageFontSize);
        }

        float bodyHeight = bodyLines > 0f ? (bodyLines * lineHeight) + 2f : 0f;
        float measured = 12f + lineHeight + bodyHeight + (bodyLines > 0f ? 4f : 0f);
        return MathF.Max(measured, lineHeight + 12f);
    }

    private static float DrawMessageBubble(int messageIndex, ChatMessage message, float x, float y, float width, float messageFontSize)
    {
        var style = Im.Style;
        float lineHeight = messageFontSize + 4f;
        float messageHeight = MeasureMessageHeight(message, width, messageFontSize);

        uint backgroundColor = ImStyle.Lerp(style.Background, style.Surface, 0.12f);
        string headerLabel = "[Assistant]";
        switch (message.Role)
        {
            case ChatRole.User:
                backgroundColor = ImStyle.WithAlpha(style.Primary, 48);
                headerLabel = "[User]";
                break;
            case ChatRole.Assistant:
                backgroundColor = ImStyle.Lerp(style.Background, style.Surface, 0.12f);
                headerLabel = "[Assistant]";
                break;
            case ChatRole.ToolUse:
                backgroundColor = ImStyle.WithAlpha(style.Secondary, 52);
                headerLabel = "[Tool] " + message.ToolName;
                break;
            case ChatRole.ToolResult:
                backgroundColor = ImStyle.WithAlpha(style.Primary, 44);
                headerLabel = "[Result] " + message.ToolName;
                break;
            case ChatRole.System:
                backgroundColor = ImStyle.WithAlpha(style.Hover, 170);
                headerLabel = "[System]";
                break;
            case ChatRole.Error:
                backgroundColor = ImStyle.WithAlpha(style.Hover, 190);
                headerLabel = "[Error]";
                break;
        }

        Im.DrawRoundedRect(x, y, width, messageHeight, 5f, backgroundColor);
        Im.DrawRoundedRectStroke(x, y, width, messageHeight, 5f, ImStyle.WithAlpha(style.Border, 210), 1f);

        float textX = x + Padding;
        float textY = y + 6f;

        bool isToolMessage = message.Role == ChatRole.ToolUse || message.Role == ChatRole.ToolResult;
        if (isToolMessage)
        {
            string togglePrefix = message.IsExpanded ? "▼ " : "▶ ";
            string interactiveHeader = togglePrefix + headerLabel;
            var headerRect = new ImRect(textX - 2f, textY - 2f, width - (Padding * 2f), lineHeight + 4f);
            bool hovered = headerRect.Contains(Im.MousePos);
            if (hovered && Im.Context.Input.MousePressed)
            {
                message.IsExpanded = !message.IsExpanded;
            }

            uint headerColor = hovered ? style.TextPrimary : style.TextSecondary;
            Im.Text(interactiveHeader.AsSpan(), textX, textY, messageFontSize, headerColor);
        }
        else
        {
            Im.Text(headerLabel.AsSpan(), textX, textY, messageFontSize, style.TextSecondary);
        }

        string body = GetMessageBody(message);
        bool showBody = !string.IsNullOrWhiteSpace(body) && (!isToolMessage || message.IsExpanded);
        if (showBody)
        {
            float bodyY = textY + lineHeight + 2f;
            float bodyHeight = MathF.Max(20f, messageHeight - (bodyY - y) - 6f);
            float bodyWidth = MathF.Max(24f, width - (Padding * 2f) - 2f);
            DrawSelectableWrappedBodyText(messageIndex, body, textX, bodyY, bodyWidth, bodyHeight, messageFontSize);
        }

        return messageHeight;
    }

    private static void DrawSelectableWrappedBodyText(int messageIndex, string bodyText, float x, float y, float width, float height, float fontSize)
    {
        MessageRenderCache cache = _messageCaches[messageIndex];
        SyncMessageCache(cache, bodyText);
        int readOnlyLength = cache.Length;
        _ = ImTextArea.DrawAt(
            cache.WidgetId,
            cache.Buffer,
            ref readOnlyLength,
            cache.Buffer.Length,
            x,
            y,
            width,
            height,
            wordWrap: true,
            fontSizePx: fontSize,
            flags: ImTextArea.ImTextAreaFlags.NoBackground |
                   ImTextArea.ImTextAreaFlags.NoBorder |
                   ImTextArea.ImTextAreaFlags.NoRounding |
                   ImTextArea.ImTextAreaFlags.NoCaret);
    }

    private static void DrawInputArea(DocWorkspace workspace, ChatSession session, ImRect inputRect, float inputFontSize)
    {
        if (inputRect.Width <= 0f || inputRect.Height <= 0f)
        {
            return;
        }

        var style = Im.Style;
        var input = Im.Context.Input;
        string inputDisabledReason = GetInputDisabledReason(workspace, session);
        bool inputDisabled = !string.IsNullOrWhiteSpace(inputDisabledReason);
        string inputBannerMessage = inputDisabledReason;
        if (string.IsNullOrWhiteSpace(inputBannerMessage) && !string.IsNullOrWhiteSpace(session.ErrorMessage))
        {
            inputBannerMessage = session.ErrorMessage;
        }
        float inputBannerHeight = string.IsNullOrWhiteSpace(inputBannerMessage) ? 0f : inputFontSize + 10f;
        float agentRowHeight = MathF.Max(24f, inputFontSize + 8f);
        float topRowHeight = MathF.Max(22f, inputFontSize + 6f);

        uint inputAreaBackground = ImStyle.Lerp(style.Background, style.Surface, 0.10f);
        Im.DrawRect(inputRect.X, inputRect.Y, inputRect.Width, inputRect.Height, inputAreaBackground);
        Im.DrawLine(inputRect.X, inputRect.Y, inputRect.Right, inputRect.Y, 1f, ImStyle.WithAlpha(style.Border, 232));

        float outerPadding = Padding;
        float innerPadding = 8f;
        float outerX = inputRect.X + outerPadding;
        float outerY = inputRect.Y + outerPadding;
        float outerWidth = MathF.Max(80f, inputRect.Width - (outerPadding * 2f));
        float outerHeight = MathF.Max(80f, inputRect.Height - (outerPadding * 2f));
        float outerCornerRadius = 6f;

        uint composerBackground = ImStyle.Lerp(style.Background, style.Surface, 0.08f);
        Im.DrawRoundedRect(outerX, outerY, outerWidth, outerHeight, outerCornerRadius, composerBackground);
        Im.DrawRoundedRectStroke(outerX, outerY, outerWidth, outerHeight, outerCornerRadius, ImStyle.WithAlpha(style.Border, 238), 1f);

        float sendButtonSize = agentRowHeight;
        float controlsY = outerY + outerHeight - innerPadding - agentRowHeight;
        float controlsX = outerX + innerPadding;
        float controlsWidth = MathF.Max(80f, outerWidth - (innerPadding * 3f) - sendButtonSize);
        float sendButtonX = outerX + outerWidth - innerPadding - sendButtonSize;
        float sendButtonY = controlsY;

        float textBottom = controlsY - InputSectionGap;
        if (inputBannerHeight > 0f)
        {
            textBottom -= (inputBannerHeight + InputSectionGap);
        }

        float textAreaX = outerX + innerPadding;
        float textAreaWidth = MathF.Max(80f, outerWidth - (innerPadding * 2f));
        float topRowY = outerY + innerPadding;
        DrawTopRow(workspace, topRowY, textAreaX, textAreaWidth, topRowHeight, inputFontSize);

        float textAreaY = topRowY + topRowHeight + ComposerTopRowGap;
        float textAreaHeight = MathF.Max(28f, textBottom - textAreaY);

        _ = ImTextArea.DrawAt(
            ChatInputWidgetId,
            InputBuffer,
            ref _inputLength,
            InputBuffer.Length,
            textAreaX,
            textAreaY,
            textAreaWidth,
            textAreaHeight,
            wordWrap: true,
            fontSizePx: inputFontSize,
            flags: ImTextArea.ImTextAreaFlags.NoBackground |
                   ImTextArea.ImTextAreaFlags.NoBorder,
            lineHeightPx: inputFontSize + 4f);

        int inputWidgetId = Im.Context.GetId(ChatInputWidgetId);
        bool inputFocused = Im.Context.IsFocused(inputWidgetId);

        if (_inputLength == 0 && !inputFocused)
        {
            Im.Text(InputPlaceholderText.AsSpan(), textAreaX + 2f, textAreaY + 2f, inputFontSize, style.TextDisabled);
        }

        BuildSuggestions(session);
        if (_suggestionCount > 0)
        {
            HandleSuggestionHotkeys(inputFocused, input);
            DrawSuggestionsPopup(textAreaX, textAreaY, textAreaWidth, textAreaHeight, inputWidgetId, inputFontSize);
        }

        bool sendClicked = DrawSendButton(sendButtonX, sendButtonY, sendButtonSize, inputFontSize, !inputDisabled);
        bool sendShortcut = inputFocused && input.KeyCtrl && input.KeyEnter;
        if ((sendClicked || sendShortcut) && !inputDisabled)
        {
            if (sendShortcut && _inputLength > 0 && InputBuffer[_inputLength - 1] == '\n')
            {
                _inputLength--;
            }

            string rawText = _inputLength > 0 ? new string(InputBuffer, 0, _inputLength) : "";
            if (workspace.TrySendChatInput(rawText))
            {
                _inputLength = 0;
                ImTextArea.SetState(inputWidgetId, 0, 0, 0);
                _suggestionCount = 0;
            }
        }

        if (inputFocused)
        {
            Im.DrawRoundedRectStroke(outerX, outerY, outerWidth, outerHeight, outerCornerRadius, ImStyle.WithAlpha(style.Primary, 220), 1f);
        }

        DrawAgentRow(workspace, session, new ImRect(
            controlsX,
            controlsY,
            controlsWidth,
            agentRowHeight));

        if (inputBannerHeight > 0f)
        {
            float bannerX = outerX + innerPadding;
            float bannerY = controlsY - InputSectionGap - inputBannerHeight;
            float bannerWidth = outerWidth - (innerPadding * 2f);
            Im.DrawRoundedRect(
                bannerX,
                bannerY,
                bannerWidth,
                inputBannerHeight,
                4f,
                ImStyle.WithAlpha(style.Hover, 170));
            Im.DrawRoundedRectStroke(
                bannerX,
                bannerY,
                bannerWidth,
                inputBannerHeight,
                4f,
                ImStyle.WithAlpha(style.Border, 255),
                1f);
            float bannerTextY = bannerY + (inputBannerHeight - inputFontSize) * 0.5f;
            Im.Text(
                inputBannerMessage.AsSpan(),
                bannerX + 6f,
                bannerTextY,
                inputFontSize,
                style.TextPrimary);
        }
    }

    private static void DrawAgentRow(DocWorkspace workspace, ChatSession session, ImRect agentRowRect)
    {
        float rowHeight = agentRowRect.Height;
        float rowY = agentRowRect.Y;
        float rowX = agentRowRect.X;
        float rowGap = 12f;
        float selectorPadding = 4f;
        string providerLabel = session.ProviderKind == ChatProviderKind.Codex ? "Codex" : "Claude";
        string agentTypeLabel = session.AgentType == ChatAgentType.Workspace ? "Workspace Agent" : "MCP Agent";
        string providerDisplay = providerLabel + " " + ChevronDownIcon;
        string agentTypeDisplay = agentTypeLabel + " " + ChevronDownIcon;
        float selectorFontSize = MathF.Max(11f, rowHeight - 10f);
        float providerWidth = Im.MeasureTextWidth(providerDisplay.AsSpan(), selectorFontSize) + (selectorPadding * 2f);
        float agentTypeWidth = Im.MeasureTextWidth(agentTypeDisplay.AsSpan(), selectorFontSize) + (selectorPadding * 2f);
        float availableWidth = MathF.Max(40f, agentRowRect.Width);
        float desiredWidth = providerWidth + rowGap + agentTypeWidth;
        if (desiredWidth > availableWidth)
        {
            float scale = availableWidth / desiredWidth;
            providerWidth *= scale;
            agentTypeWidth *= scale;
        }
        float providerX = rowX;
        float agentTypeX = providerX + providerWidth + rowGap;

        bool providerPressed = DrawTextSelector(
            providerDisplay,
            "chat_provider_selector",
            providerX,
            rowY,
            providerWidth,
            rowHeight,
            selectorFontSize);
        if (providerPressed)
        {
            ImContextMenu.OpenAt(ProviderMenuId, providerX, rowY + rowHeight);
        }

        if (ImContextMenu.Begin(ProviderMenuId))
        {
            if (ImContextMenu.Item("Claude") && session.ProviderKind != ChatProviderKind.Claude)
            {
                workspace.SwitchChatProvider(ChatProviderKind.Claude);
            }

            if (ImContextMenu.Item("Codex") && session.ProviderKind != ChatProviderKind.Codex)
            {
                workspace.SwitchChatProvider(ChatProviderKind.Codex);
            }

            ImContextMenu.End();
        }

        bool agentTypePressed = DrawTextSelector(
            agentTypeDisplay,
            "chat_agent_type_selector",
            agentTypeX,
            rowY,
            agentTypeWidth,
            rowHeight,
            selectorFontSize);
        if (agentTypePressed)
        {
            ImContextMenu.OpenAt(AgentTypeMenuId, agentTypeX, rowY + rowHeight);
        }

        if (ImContextMenu.Begin(AgentTypeMenuId))
        {
            if (ImContextMenu.Item("MCP Agent") && session.AgentType != ChatAgentType.Mcp)
            {
                workspace.SwitchChatAgentType(ChatAgentType.Mcp);
            }

            if (ImContextMenu.Item("Workspace Agent") && session.AgentType != ChatAgentType.Workspace)
            {
                workspace.SwitchChatAgentType(ChatAgentType.Workspace);
            }

            ImContextMenu.End();
        }
    }

    private static bool DrawSendButton(float x, float y, float size, float iconFontSize, bool enabled)
    {
        int widgetId = Im.Context.GetId("chat_send_outlined_button");
        var widgetRect = new ImRect(x, y, size, size);
        bool hovered = widgetRect.Contains(Im.MousePos);
        var input = Im.Context.Input;
        if (hovered)
        {
            Im.Context.SetHot(widgetId);
        }

        if (enabled && hovered && input.MousePressed)
        {
            Im.Context.SetActive(widgetId);
        }

        bool pressed = false;
        if (Im.Context.IsActive(widgetId) && input.MouseReleased)
        {
            pressed = enabled && hovered;
            Im.Context.ClearActive();
        }

        var style = Im.Style;
        uint borderColor = enabled
            ? ImStyle.WithAlpha(style.Border, hovered ? (byte)255 : (byte)220)
            : ImStyle.WithAlpha(style.Border, 160);
        uint iconColor = enabled ? (hovered ? style.TextPrimary : style.TextSecondary) : style.TextDisabled;
        if (enabled && hovered)
        {
            Im.DrawRoundedRect(x, y, size, size, 5f, ImStyle.WithAlpha(style.Hover, 180));
        }

        Im.DrawRoundedRectStroke(x, y, size, size, 5f, borderColor, 1f);
        float textX = x + (size - iconFontSize) * 0.5f;
        float textY = y + (size - iconFontSize) * 0.5f;
        Im.Text(SendIcon.AsSpan(), textX, textY, iconFontSize, iconColor);
        return pressed;
    }

    private static bool DrawTextSelector(string label, string widgetIdSuffix, float x, float y, float width, float height, float fontSize)
    {
        if (width <= 0f || height <= 0f)
        {
            return false;
        }

        int widgetId = Im.Context.GetId(widgetIdSuffix);
        var selectorRect = new ImRect(x, y, width, height);
        bool hovered = selectorRect.Contains(Im.MousePos);
        var input = Im.Context.Input;
        if (hovered)
        {
            Im.Context.SetHot(widgetId);
        }

        if (hovered && input.MousePressed)
        {
            Im.Context.SetActive(widgetId);
        }

        bool pressed = false;
        if (Im.Context.IsActive(widgetId) && input.MouseReleased)
        {
            pressed = hovered;
            Im.Context.ClearActive();
        }

        var style = Im.Style;
        uint textColor = hovered ? style.TextPrimary : style.TextSecondary;
        float textY = y + (height - fontSize) * 0.5f;
        Im.Text(label.AsSpan(), x, textY, fontSize, textColor);
        if (hovered)
        {
            Im.DrawLine(x, y + height - 2f, x + width, y + height - 2f, 1f, ImStyle.WithAlpha(style.Primary, 210));
        }

        return pressed;
    }

    private static void DrawTopRow(DocWorkspace workspace, float y, float x, float width, float rowHeight, float fontSize)
    {
        var style = Im.Style;
        float iconSize = MathF.Max(12f, fontSize - 1f);
        float iconY = y + (rowHeight - iconSize) * 0.5f;
        Im.Text(AttachmentIcon.AsSpan(), x, iconY, iconSize, style.TextSecondary);

        string contextLabel = BuildContextChipLabel(workspace);
        float chipX = x + iconSize + 6f;
        float chipPadding = 6f;
        float chipHeight = MathF.Max(18f, rowHeight - 2f);
        string chipText = PlusIcon + "  " + contextLabel;
        float availableChipWidth = MathF.Max(0f, width - (chipX - x));
        float chipWidth = MathF.Min(availableChipWidth, Im.MeasureTextWidth(chipText.AsSpan(), fontSize - 1f) + (chipPadding * 2f));
        if (chipWidth <= 24f)
        {
            return;
        }

        float chipY = y + (rowHeight - chipHeight) * 0.5f;
        uint chipBackground = ImStyle.Lerp(style.Background, style.Surface, 0.06f);
        Im.DrawRoundedRect(chipX, chipY, chipWidth, chipHeight, 4f, chipBackground);
        Im.DrawRoundedRectStroke(chipX, chipY, chipWidth, chipHeight, 4f, ImStyle.WithAlpha(style.Border, 235), 1f);
        float chipTextY = chipY + (chipHeight - (fontSize - 1f)) * 0.5f;
        Im.Text(chipText.AsSpan(), chipX + chipPadding, chipTextY, fontSize - 1f, style.TextSecondary);
    }

    private static string BuildContextChipLabel(DocWorkspace workspace)
    {
        if (workspace.ActiveView == ActiveViewKind.Document && workspace.ActiveDocument != null)
        {
            if (!string.IsNullOrWhiteSpace(workspace.ActiveDocument.Title))
            {
                return workspace.ActiveDocument.Title;
            }

            return workspace.ActiveDocument.FileName;
        }

        if (workspace.ActiveTable != null)
        {
            if (!string.IsNullOrWhiteSpace(workspace.ActiveTable.Name))
            {
                return workspace.ActiveTable.Name;
            }

            return workspace.ActiveTable.FileName;
        }

        return "Workspace context";
    }

    private static string GetInputDisabledReason(DocWorkspace workspace, ChatSession session)
    {
        if (workspace.IsDirty)
        {
            return "Chat disabled: unsaved local changes.";
        }

        if (session.IsProcessing)
        {
            return "Assistant is still responding.";
        }

        return "";
    }

    private static void BuildSuggestions(ChatSession session)
    {
        _suggestionCount = 0;
        if (_inputLength <= 0 || InputBuffer[0] != '/')
        {
            return;
        }

        string inputText = new string(InputBuffer, 0, _inputLength);
        int firstSpace = inputText.IndexOf(' ');
        if (firstSpace < 0)
        {
            _suggestionCount = ChatSlashCommandParser.BuildSuggestions(inputText, session.ProviderCommands, _suggestions);
            _selectedSuggestionIndex = Math.Clamp(_selectedSuggestionIndex, 0, Math.Max(0, _suggestionCount - 1));
            return;
        }

        string commandName = inputText[..firstSpace];
        string argumentPrefix = inputText[(firstSpace + 1)..].TrimStart();
        if (string.Equals(commandName, "/model", StringComparison.OrdinalIgnoreCase))
        {
            _suggestionCount = BuildOptionSuggestions(argumentPrefix, session.ModelOptions);
        }
        else if (string.Equals(commandName, "/provider", StringComparison.OrdinalIgnoreCase))
        {
            _suggestionCount = BuildOptionSuggestions(argumentPrefix, ProviderOptions);
        }
        else if (string.Equals(commandName, "/agent", StringComparison.OrdinalIgnoreCase))
        {
            _suggestionCount = BuildOptionSuggestions(argumentPrefix, AgentTypeOptions);
        }

        _selectedSuggestionIndex = Math.Clamp(_selectedSuggestionIndex, 0, Math.Max(0, _suggestionCount - 1));
    }

    private static int BuildOptionSuggestions(string prefix, IReadOnlyList<string> options)
    {
        int count = 0;
        for (int optionIndex = 0; optionIndex < options.Count; optionIndex++)
        {
            string option = options[optionIndex];
            if (!string.IsNullOrWhiteSpace(prefix) &&
                !option.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (count >= _suggestions.Length)
            {
                break;
            }

            _suggestions[count] = option;
            count++;
        }

        return count;
    }

    private static void HandleSuggestionHotkeys(bool inputFocused, DerpLib.ImGui.Input.ImInput input)
    {
        if (!inputFocused || _suggestionCount == 0)
        {
            return;
        }

        if (input.KeyDown)
        {
            _selectedSuggestionIndex++;
            if (_selectedSuggestionIndex >= _suggestionCount)
            {
                _selectedSuggestionIndex = 0;
            }
        }
        else if (input.KeyUp)
        {
            _selectedSuggestionIndex--;
            if (_selectedSuggestionIndex < 0)
            {
                _selectedSuggestionIndex = _suggestionCount - 1;
            }
        }
        else if (input.KeyTab)
        {
            ApplySuggestion(_selectedSuggestionIndex);
        }
    }

    private static void DrawSuggestionsPopup(
        float textAreaX,
        float textAreaY,
        float textAreaWidth,
        float textAreaHeight,
        int inputWidgetId,
        float popupFontSize)
    {
        if (_suggestionCount == 0)
        {
            return;
        }

        var style = Im.Style;
        float rowHeight = popupFontSize + 8f;
        int visibleRows = Math.Min(_suggestionCount, 6);
        float popupHeight = (visibleRows * rowHeight) + 6f;
        float popupWidth = MathF.Max(180f, textAreaWidth * 0.6f);
        float popupX = textAreaX;
        float popupY = textAreaY - popupHeight - 4f;
        if (popupY < Im.WindowContentRect.Y)
        {
            popupY = textAreaY + textAreaHeight + 4f;
        }

        uint popupBackground = ImStyle.Lerp(style.Background, style.Surface, 0.06f);
        Im.DrawRoundedRect(popupX, popupY, popupWidth, popupHeight, 5f, popupBackground);
        Im.DrawRoundedRectStroke(popupX, popupY, popupWidth, popupHeight, 5f, ImStyle.WithAlpha(style.Border, 232), 1f);

        float drawY = popupY + 3f;
        for (int suggestionIndex = 0; suggestionIndex < visibleRows; suggestionIndex++)
        {
            var rowRect = new ImRect(popupX + 2f, drawY, popupWidth - 4f, rowHeight);
            bool hovered = rowRect.Contains(Im.MousePos);
            bool selected = suggestionIndex == _selectedSuggestionIndex;
            if (hovered || selected)
            {
                Im.DrawRoundedRect(rowRect.X, rowRect.Y, rowRect.Width, rowRect.Height, 3f, ImStyle.WithAlpha(style.Hover, 180));
            }

            if (hovered && Im.Context.Input.MousePressed)
            {
                _selectedSuggestionIndex = suggestionIndex;
                ApplySuggestion(suggestionIndex);
                Im.Context.RequestFocus(inputWidgetId);
                Im.Context.SetActive(inputWidgetId);
            }

            Im.Text(_suggestions[suggestionIndex].AsSpan(), rowRect.X + 6f, rowRect.Y + 4f, popupFontSize, style.TextPrimary);
            drawY += rowHeight;
        }
    }

    private static void ApplySuggestion(int suggestionIndex)
    {
        if (suggestionIndex < 0 || suggestionIndex >= _suggestionCount)
        {
            return;
        }

        string suggestion = _suggestions[suggestionIndex];
        string currentInput = _inputLength > 0 ? new string(InputBuffer, 0, _inputLength) : "";
        int firstSpace = currentInput.IndexOf(' ');
        string replacementText;
        if (firstSpace < 0)
        {
            replacementText = suggestion + " ";
        }
        else
        {
            string commandName = currentInput[..firstSpace];
            replacementText = commandName + " " + suggestion;
        }

        SetInputText(replacementText);
    }

    private static void SetInputText(string text)
    {
        int copyLength = Math.Min(text.Length, InputBuffer.Length);
        text.AsSpan(0, copyLength).CopyTo(InputBuffer);
        _inputLength = copyLength;

        int inputWidgetId = Im.Context.GetId(ChatInputWidgetId);
        ImTextArea.SetState(inputWidgetId, _inputLength);
    }

    private static string GetMessageBody(ChatMessage message)
    {
        if (message.Role == ChatRole.ToolUse)
        {
            return message.ToolInput;
        }

        return message.Content;
    }

    private static void EnsureMessageCacheCount(int count)
    {
        while (_messageCaches.Count < count)
        {
            _messageCaches.Add(new MessageRenderCache(_messageCaches.Count));
        }
    }

    private static void SyncMessageCache(MessageRenderCache cache, string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            cache.Length = 0;
            return;
        }

        int copyLength = sourceText.Length;
        if (cache.Buffer.Length < copyLength)
        {
            int newSize = cache.Buffer.Length;
            if (newSize < 128)
            {
                newSize = 128;
            }

            while (newSize < copyLength)
            {
                newSize *= 2;
            }

            cache.Buffer = new char[newSize];
        }

        sourceText.AsSpan().CopyTo(cache.Buffer);
        cache.Length = copyLength;
    }

    private static int CountWrappedLines(string text, float maxWidth, float fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        if (maxWidth <= 1f)
        {
            return 1;
        }

        int lines = 1;
        float lineWidth = 0f;
        for (int charIndex = 0; charIndex < text.Length; charIndex++)
        {
            char character = text[charIndex];
            if (character == '\r')
            {
                continue;
            }

            if (character == '\n')
            {
                lines++;
                lineWidth = 0f;
                continue;
            }

            float glyphWidth = Im.MeasureTextWidth(text.AsSpan(charIndex, 1), fontSize);
            if (lineWidth > 0f && lineWidth + glyphWidth > maxWidth)
            {
                lines++;
                lineWidth = glyphWidth;
            }
            else
            {
                lineWidth += glyphWidth;
            }
        }

        return lines;
    }
}
