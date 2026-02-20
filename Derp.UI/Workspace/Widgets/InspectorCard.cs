using System;
using System.Numerics;
using DerpLib.ImGui;
using DerpLib.ImGui.Core;
using DerpLib.ImGui.Layout;
using DerpLib.ImGui.Rendering;

namespace Derp.UI;

	internal static class InspectorCard
	{
	    private const float CardCornerRadius = 10f;
	    private const float CardPadding = 12f;
	    private const float CardGap = 4f;
	    private const float HeaderHeight = 32f;

    private struct Frame
    {
        public int CardId;
        public Vector2 StartCursor;
        public float Width;
    }

    private static readonly Frame[] FrameStack = new Frame[16];
    private static int _frameDepth;

    public static void Begin(string title)
    {
        int cardId = Im.Context.GetId(title);

        Vector2 startCursor = ImLayout.GetCursor();
        float width = ImLayout.RemainingWidth();
        if (width <= 0f)
        {
            width = 240f;
        }

        var frame = new Frame
        {
            CardId = cardId,
            StartCursor = startCursor,
            Width = width
        };

        if (_frameDepth >= FrameStack.Length)
        {
            _frameDepth = FrameStack.Length - 1;
        }
        FrameStack[_frameDepth++] = frame;

	        var headerRect = ImLayout.AllocateRect(0f, HeaderHeight);
	        DrawHeader(title, headerRect);
	        ImLayout.Space(4f);
	    }

    public static void End()
    {
        if (_frameDepth <= 0)
        {
            return;
        }

        ImLayout.Space(CardPadding);

        Frame frame = FrameStack[--_frameDepth];

        Vector2 endCursor = ImLayout.GetCursor();
        float height = endCursor.Y - frame.StartCursor.Y;
        height = MathF.Max(HeaderHeight, height - Im.Style.Spacing);

        var rect = new ImRect(frame.StartCursor.X, frame.StartCursor.Y, frame.Width, height);

        ImDrawLayer previousLayer = Im.Context.CurrentLayer;
        Im.SetDrawLayer(ImDrawLayer.Background);
        DrawCardBackground(rect);
        Im.SetDrawLayer(previousLayer);

        ImLayout.Space(CardGap);
    }

    public static void DrawInspectorBackground(ImRect rect)
    {
        ImDrawLayer previousLayer = Im.Context.CurrentLayer;
        Im.SetDrawLayer(ImDrawLayer.Background);
        Im.DrawRect(rect.X, rect.Y, rect.Width, rect.Height, Im.Style.Background);
        Im.SetDrawLayer(previousLayer);
    }

	    private static void DrawCardBackground(ImRect rect)
	    {
	        uint cardColor = ImStyle.Lerp(Im.Style.Background, 0xFFFFFFFF, 0.06f);
	        uint border = ImStyle.WithAlphaF(Im.Style.Border, 0.65f);
	        Im.DrawRoundedRect(rect.X, rect.Y, rect.Width, rect.Height, CardCornerRadius, cardColor);
	        Im.DrawRoundedRectStroke(rect.X, rect.Y, rect.Width, rect.Height, CardCornerRadius, border, Im.Style.BorderWidth);
	    }

    private static void DrawHeader(string title, ImRect headerRect)
    {
        float textY = headerRect.Y + (headerRect.Height - Im.Style.FontSize) * 0.5f;
        float textX = headerRect.X + CardPadding;
        Im.Text(title.AsSpan(), textX, textY, Im.Style.FontSize, Im.Style.TextPrimary);
    }
}
