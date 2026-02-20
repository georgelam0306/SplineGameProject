namespace Derp.Doc.Mcp;

internal interface IDerpDocNanobananaClient
{
    bool TryInvoke(
        string apiBaseUrl,
        string endpointPath,
        string apiKey,
        string requestJson,
        out byte[] imageBytes,
        out string responseJson,
        out string errorMessage);
}
