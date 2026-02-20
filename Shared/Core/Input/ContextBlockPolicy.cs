using System;

namespace Core.Input;

/// <summary>
/// Determines how a context blocks input to contexts below it on the stack.
/// </summary>
public enum ContextBlockMode
{
    /// <summary>Additive - all contexts below still receive input.</summary>
    None,
    /// <summary>Block everything below this context.</summary>
    All,
    /// <summary>Only specified contexts below receive input (whitelist).</summary>
    Whitelist,
    /// <summary>Specified contexts below are blocked (blacklist).</summary>
    Blacklist
}

/// <summary>
/// Defines the blocking policy for an input context.
/// </summary>
public readonly record struct ContextBlockPolicy
{
    private const int MaxContexts = 8;

    public ContextBlockMode Mode { get; init; }

    // Fixed-size array to avoid allocations
    private readonly StringHandle[] _contexts;
    private readonly int _contextCount;

    private ContextBlockPolicy(ContextBlockMode mode, StringHandle[] contexts, int count)
    {
        Mode = mode;
        _contexts = contexts;
        _contextCount = count;
    }

    /// <summary>No blocking - all contexts below receive input.</summary>
    public static ContextBlockPolicy None => new(ContextBlockMode.None, Array.Empty<StringHandle>(), 0);

    /// <summary>Block all contexts below.</summary>
    public static ContextBlockPolicy BlockAll => new(ContextBlockMode.All, Array.Empty<StringHandle>(), 0);

    /// <summary>Only allow specified contexts to receive input (whitelist).</summary>
    public static ContextBlockPolicy Allow(params string[] contexts)
    {
        var handles = new StringHandle[Math.Min(contexts.Length, MaxContexts)];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = contexts[i];
        }
        return new ContextBlockPolicy(ContextBlockMode.Whitelist, handles, handles.Length);
    }

    /// <summary>Block specified contexts from receiving input (blacklist).</summary>
    public static ContextBlockPolicy Block(params string[] contexts)
    {
        var handles = new StringHandle[Math.Min(contexts.Length, MaxContexts)];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = contexts[i];
        }
        return new ContextBlockPolicy(ContextBlockMode.Blacklist, handles, handles.Length);
    }

    /// <summary>
    /// Check if a context name is blocked by this policy.
    /// </summary>
    public bool IsBlocked(StringHandle contextName)
    {
        return Mode switch
        {
            ContextBlockMode.None => false,
            ContextBlockMode.All => true,
            ContextBlockMode.Whitelist => !ContainsContext(contextName),
            ContextBlockMode.Blacklist => ContainsContext(contextName),
            _ => false
        };
    }

    private bool ContainsContext(StringHandle name)
    {
        for (int i = 0; i < _contextCount; i++)
        {
            if (_contexts[i] == name)
            {
                return true;
            }
        }
        return false;
    }
}
