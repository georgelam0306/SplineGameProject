using System;
using DerpLib.AssetPipeline;

namespace Derp.UI;

public static class UiRuntimeContent
{
    public static void Register(ContentManager content)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        content.RegisterType<CompiledUi>();
        content.RegisterReader<CompiledUi>(new CompiledUiContentReader());
    }
}

