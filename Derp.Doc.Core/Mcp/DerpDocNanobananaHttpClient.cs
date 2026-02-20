using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Derp.Doc.Mcp;

internal sealed class DerpDocNanobananaHttpClient : IDerpDocNanobananaClient
{
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    public bool TryInvoke(
        string apiBaseUrl,
        string endpointPath,
        string apiKey,
        string requestJson,
        out byte[] imageBytes,
        out string responseJson,
        out string errorMessage)
    {
        imageBytes = Array.Empty<byte>();
        responseJson = "{}";
        errorMessage = "";

        if (!TryBuildEndpointUri(apiBaseUrl, endpointPath, out string endpointUri, out errorMessage))
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri);
            request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = SharedHttpClient.Send(request);
            byte[] payload = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                string bodySnippet = payload.Length > 0
                    ? Truncate(Encoding.UTF8.GetString(payload), 400)
                    : "";
                errorMessage = string.IsNullOrWhiteSpace(bodySnippet)
                    ? "Nanobanana request failed with HTTP " + (int)response.StatusCode + "."
                    : "Nanobanana request failed with HTTP " + (int)response.StatusCode + ": " + bodySnippet;
                return false;
            }

            string mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                imageBytes = payload;
                responseJson = "{}";
                return true;
            }

            string json = Encoding.UTF8.GetString(payload);
            if (string.IsNullOrWhiteSpace(json))
            {
                errorMessage = "Nanobanana response body is empty.";
                return false;
            }

            responseJson = json;
            if (!TryExtractImageBytesFromResponseJson(json, out imageBytes, out errorMessage))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = "Nanobanana request failed: " + ex.Message;
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(120);
        return client;
    }

    private static bool TryBuildEndpointUri(
        string apiBaseUrl,
        string endpointPath,
        out string endpointUri,
        out string errorMessage)
    {
        endpointUri = "";
        errorMessage = "";

        string normalizedBaseUrl = (apiBaseUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
        {
            errorMessage = "Missing nanobanana base URL.";
            return false;
        }

        string normalizedEndpointPath = string.IsNullOrWhiteSpace(endpointPath)
            ? ""
            : endpointPath.Trim();

        if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            errorMessage = "Nanobanana base URL is invalid: " + normalizedBaseUrl;
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalizedEndpointPath))
        {
            endpointUri = baseUri.ToString();
            return true;
        }

        if (normalizedEndpointPath.StartsWith(":", StringComparison.Ordinal))
        {
            endpointUri = normalizedBaseUrl.TrimEnd('/') + normalizedEndpointPath;
            return true;
        }

        if (!normalizedBaseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            normalizedBaseUrl += "/";
        }

        normalizedEndpointPath = normalizedEndpointPath.TrimStart('/');

        Uri resolvedUri = string.IsNullOrWhiteSpace(normalizedEndpointPath)
            ? baseUri
            : new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), normalizedEndpointPath);
        endpointUri = resolvedUri.ToString();
        return true;
    }

    private static bool TryExtractImageBytesFromResponseJson(
        string responseJson,
        out byte[] imageBytes,
        out string errorMessage)
    {
        imageBytes = Array.Empty<byte>();
        errorMessage = "";

        try
        {
            using var document = JsonDocument.Parse(responseJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Nanobanana response must be a JSON object.";
                return false;
            }

            if (TryExtractImageFromGeminiCandidates(
                    root,
                    out imageBytes,
                    out string geminiErrorMessage,
                    out bool hasGeminiCandidates))
            {
                return true;
            }

            if (hasGeminiCandidates)
            {
                errorMessage = geminiErrorMessage;
                return false;
            }

            if (TryExtractImageFromStringProperty(root, "imageBase64", out imageBytes, out errorMessage))
            {
                return true;
            }

            if (TryExtractImageFromStringProperty(root, "imageDataUrl", out imageBytes, out errorMessage))
            {
                return true;
            }

            if (TryExtractImageFromStringProperty(root, "image", out imageBytes, out errorMessage))
            {
                return true;
            }

            if (TryExtractImageFromArrayObjectProperty(root, "data", out imageBytes, out errorMessage))
            {
                return true;
            }

            if (TryExtractImageFromArrayObjectProperty(root, "images", out imageBytes, out errorMessage))
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            errorMessage = "Failed parsing nanobanana JSON response: " + ex.Message;
            return false;
        }

        errorMessage = "Response did not include Gemini inlineData image bytes or known image base64 fields.";
        return false;
    }

    private static bool TryExtractImageFromGeminiCandidates(
        JsonElement root,
        out byte[] imageBytes,
        out string errorMessage,
        out bool hasGeminiCandidates)
    {
        imageBytes = Array.Empty<byte>();
        errorMessage = "";
        hasGeminiCandidates = false;

        if (!root.TryGetProperty("candidates", out JsonElement candidatesElement) ||
            candidatesElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        hasGeminiCandidates = true;
        if (candidatesElement.GetArrayLength() <= 0)
        {
            if (root.TryGetProperty("promptFeedback", out JsonElement promptFeedbackElement))
            {
                errorMessage = "Gemini returned no candidates: " + Truncate(promptFeedbackElement.GetRawText(), 400);
            }
            else
            {
                errorMessage = "Gemini returned no candidates.";
            }

            return false;
        }

        foreach (JsonElement candidateElement in candidatesElement.EnumerateArray())
        {
            if (candidateElement.ValueKind != JsonValueKind.Object ||
                !candidateElement.TryGetProperty("content", out JsonElement contentElement) ||
                contentElement.ValueKind != JsonValueKind.Object ||
                !contentElement.TryGetProperty("parts", out JsonElement partsElement) ||
                partsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement partElement in partsElement.EnumerateArray())
            {
                if (partElement.ValueKind != JsonValueKind.Object ||
                    !partElement.TryGetProperty("inlineData", out JsonElement inlineDataElement) ||
                    inlineDataElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!inlineDataElement.TryGetProperty("data", out JsonElement dataElement) ||
                    dataElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string encodedPayload = dataElement.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(encodedPayload))
                {
                    continue;
                }

                try
                {
                    imageBytes = Convert.FromBase64String(encodedPayload.Trim());
                    return true;
                }
                catch (FormatException)
                {
                    errorMessage = "Gemini inlineData.data is not valid base64.";
                    return false;
                }
            }
        }

        errorMessage = "Gemini response did not include an image inlineData part.";
        return false;
    }

    private static bool TryExtractImageFromArrayObjectProperty(
        JsonElement root,
        string propertyName,
        out byte[] imageBytes,
        out string errorMessage)
    {
        imageBytes = Array.Empty<byte>();
        errorMessage = "";

        if (!root.TryGetProperty(propertyName, out JsonElement arrayElement) ||
            arrayElement.ValueKind != JsonValueKind.Array ||
            arrayElement.GetArrayLength() <= 0)
        {
            return false;
        }

        JsonElement firstItem = arrayElement[0];
        if (firstItem.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (TryExtractImageFromStringProperty(firstItem, "b64_json", out imageBytes, out errorMessage))
        {
            return true;
        }

        if (TryExtractImageFromStringProperty(firstItem, "base64", out imageBytes, out errorMessage))
        {
            return true;
        }

        if (TryExtractImageFromStringProperty(firstItem, "imageBase64", out imageBytes, out errorMessage))
        {
            return true;
        }

        if (TryExtractImageFromStringProperty(firstItem, "imageDataUrl", out imageBytes, out errorMessage))
        {
            return true;
        }

        if (TryExtractImageFromStringProperty(firstItem, "image", out imageBytes, out errorMessage))
        {
            return true;
        }

        return false;
    }

    private static bool TryExtractImageFromStringProperty(
        JsonElement root,
        string propertyName,
        out byte[] imageBytes,
        out string errorMessage)
    {
        imageBytes = Array.Empty<byte>();
        errorMessage = "";

        if (!root.TryGetProperty(propertyName, out JsonElement propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string encodedPayload = propertyElement.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(encodedPayload))
        {
            errorMessage = "Nanobanana response field '" + propertyName + "' is empty.";
            return false;
        }

        string normalizedPayload = encodedPayload.Trim();
        if (normalizedPayload.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int commaIndex = normalizedPayload.IndexOf(',');
            if (commaIndex < 0 || commaIndex == normalizedPayload.Length - 1)
            {
                errorMessage = "Nanobanana image data URL is invalid.";
                return false;
            }

            normalizedPayload = normalizedPayload[(commaIndex + 1)..];
        }

        try
        {
            imageBytes = Convert.FromBase64String(normalizedPayload);
            return true;
        }
        catch (FormatException)
        {
            errorMessage = "Nanobanana response field '" + propertyName + "' is not valid base64.";
            return false;
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return text.Length <= maxLength
            ? text
            : text[..maxLength] + "...";
    }
}
