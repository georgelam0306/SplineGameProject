namespace DerpLib.ImGui;

/// <summary>
/// Rolling history buffer for graph data.
/// This is a data-only container - use Im.DrawGraph() for rendering.
/// </summary>
public sealed class ImGraph
{
    private readonly float[] _values;
    private readonly int _capacity;
    private int _head;
    private int _count;
    private float _min;
    private float _max;
    private bool _autoScale;

    /// <summary>Current number of values in the buffer.</summary>
    public int Count => _count;

    /// <summary>Maximum capacity of the buffer.</summary>
    public int Capacity => _capacity;

    /// <summary>Minimum value for Y-axis scaling.</summary>
    public float Min => _min;

    /// <summary>Maximum value for Y-axis scaling.</summary>
    public float Max => _max;

    /// <summary>Most recent value added.</summary>
    public float Current => _count > 0 ? _values[(_head - 1 + _capacity) % _capacity] : 0f;

    /// <summary>
    /// Create a new graph data buffer with the specified capacity.
    /// </summary>
    /// <param name="capacity">Number of samples to store.</param>
    /// <param name="minValue">Minimum Y value (or 0 for auto-scale).</param>
    /// <param name="maxValue">Maximum Y value (or 0 for auto-scale).</param>
    public ImGraph(int capacity, float minValue = 0f, float maxValue = 0f)
    {
        _capacity = capacity;
        _values = new float[capacity];
        _min = minValue;
        _max = maxValue;
        _autoScale = minValue == 0f && maxValue == 0f;
    }

    /// <summary>
    /// Add a value to the rolling history.
    /// </summary>
    public void Push(float value)
    {
        _values[_head] = value;
        _head = (_head + 1) % _capacity;
        if (_count < _capacity) _count++;

        // Update auto-scale bounds
        if (_autoScale)
        {
            if (_count == 1)
            {
                _min = value;
                _max = value;
            }
            else
            {
                _min = Math.Min(_min, value);
                _max = Math.Max(_max, value);
            }
        }
    }

    /// <summary>
    /// Clear all values.
    /// </summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
        if (_autoScale)
        {
            _min = 0;
            _max = 0;
        }
    }

    /// <summary>
    /// Set fixed Y-axis range (disables auto-scale).
    /// </summary>
    public void SetRange(float min, float max)
    {
        _min = min;
        _max = max;
        _autoScale = false;
    }

    /// <summary>
    /// Enable auto-scaling based on data range.
    /// </summary>
    public void EnableAutoScale()
    {
        _autoScale = true;
        RecalculateBounds();
    }

    /// <summary>
    /// Get values in chronological order (oldest to newest).
    /// </summary>
    public void GetValues(Span<float> output)
    {
        int toCopy = Math.Min(output.Length, _count);
        int start = (_head - _count + _capacity) % _capacity;

        for (int i = 0; i < toCopy; i++)
        {
            output[i] = _values[(start + i) % _capacity];
        }
    }

    private void RecalculateBounds()
    {
        if (_count == 0)
        {
            _min = 0;
            _max = 0;
            return;
        }

        _min = float.MaxValue;
        _max = float.MinValue;

        int start = (_head - _count + _capacity) % _capacity;
        for (int i = 0; i < _count; i++)
        {
            float v = _values[(start + i) % _capacity];
            _min = Math.Min(_min, v);
            _max = Math.Max(_max, v);
        }
    }
}
