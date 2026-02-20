namespace DerpLib.AssetPipeline;

public static class ContentReaderDiscovery
{
    public static void DiscoverInto(IContentReaderRegistry registry, Action<string>? log = null)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            foreach (var t in types)
            {
                if (t.IsAbstract || t.IsInterface) continue;
                var attrs = t.GetCustomAttributes(typeof(ContentReaderAttribute), inherit: false);
                if (attrs.Length == 0) continue;
                object? instance = null;
                try { instance = Activator.CreateInstance(t); }
                catch { continue; }
                foreach (ContentReaderAttribute attr in attrs)
                {
                    registry.Register(attr.RuntimeType, instance!);
                    log?.Invoke($"[reader-discovery] {t.Name} for {attr.RuntimeType.Name}");
                }
            }
        }
    }
}
