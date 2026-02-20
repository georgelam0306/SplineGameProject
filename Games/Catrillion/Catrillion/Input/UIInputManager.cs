using System.Collections.Generic;
using System.Numerics;
using Catrillion.Stores;
using Raylib_cs;

namespace Catrillion.Input;

public class UIInputManager
{
    private bool _mouseConsumed;
    private readonly Dictionary<string, Rectangle> _uiRects;
    private readonly InputStore _inputStore;

    public UIInputManager(InputStore inputStore)
    {
        _uiRects = new Dictionary<string, Rectangle>(8);
        _inputStore = inputStore;
    }

    public void BeginFrame()
    {
        _mouseConsumed = false;
    }

    public void RegisterUIRect(string name, Rectangle rect)
    {
        _uiRects[name] = rect;
    }

    public void UnregisterUIRect(string name)
    {
        _uiRects.Remove(name);
    }

    public void ConsumeMouse()
    {
        _mouseConsumed = true;
    }

    public bool IsMouseOverUI()
    {
        Vector2 mousePos = _inputStore.InputManager.Device.MousePosition;
        foreach (var kvp in _uiRects)
        {
            if (Raylib.CheckCollisionPointRec(mousePos, kvp.Value))
            {
                return true;
            }
        }
        return false;
    }

    public bool IsMouseAvailable()
    {
        if (_mouseConsumed)
        {
            return false;
        }

        return !IsMouseOverUI();
    }
}

