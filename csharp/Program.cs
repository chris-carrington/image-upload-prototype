using SkiaSharp;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;


class Program
{
    public static async Task Main ()
    {
        var listener = StartServer();

        while (true) // used to continuously listen for and handle incoming requests (keeps server running)
        {
            var (request, response) = await GetContextAsync(listener);

            try
            {
                SetResponseHeaders(response);

                if (request.HttpMethod == "OPTIONS")
                {
                    OnOptions(response);
                    continue;  // makes sure the while(true) loop skips further processing for OPTIONS request, so no additional logic is executed for this type of request.
                }

                var requestPath = GetRequestPath(request);

                if (requestPath == "/image-upload") ProcessImageUploadRequest(request, response);
                else ProcessImagesRequest(request, response, requestPath);
            }
            catch (Exception exception)
            {
                OnError(response, exception.Message);
                continue;
            }
        }
    }


    private static async void ProcessImageUploadRequest (HttpListenerRequest request, HttpListenerResponse response)
    {
        var contentType = ValidateImageUploadRequest(request);
        var boundary = GetBoundary(contentType);
        var requestBody = await ParseMultipartFormDataAsync(request.InputStream, boundary);
        var filename = await SaveImage(requestBody);
        var customerIdAsString = Encoding.UTF8.GetString(requestBody.CustomerIdBytes); // bytes to string

        OnImageUploadSuccess(response, filename, customerIdAsString);
    }


    private static string GetBoundary (string contentType) // Example Header => content-type: multipart/form-data; boundary=----WebKitFormBoundaryECOAm72ObCFGjWIt
    {
        string[] elements = contentType.Split(';');

        if (elements.Length != 2) throw new Exception("Please ensure the content-type header has only one semi colon in it, good example: multipart/form-data; boundary=----WebKitFormBoundaryECOAm72ObCFGjWIt");        
        if (!elements[1].Trim().StartsWith("boundary=")) throw new Exception("Please ensure that after the semicolon, \"boundary=\" is present, good example: multipart/form-data; boundary=----WebKitFormBoundaryECOAm72ObCFGjWIt");

        string boundary = elements[1].Split('=')[1].Trim('"'); // The .Trim('"') method removes any double-quote (") characters from the start and end of the string. Some boundary values in the content-type header might be enclosed in double quotes, as allowed by the HTTP specification. For example: content-type: multipart/form-data; boundary="----WebKitFormBoundaryECOAm72ObCFGjWIt"

        if (string.IsNullOrWhiteSpace(boundary)) throw new Exception("Please ensure the content-type header boundary is not null and is not an empty string, good example: multipart/form-data; boundary=----WebKitFormBoundaryECOAm72ObCFGjWIt");

        return boundary;
    }


    private static void ProcessImagesRequest (HttpListenerRequest request, HttpListenerResponse response, string requestPath)
    {
        ValidateImagesRequest(request);

        string imagePath = requestPath[1..]; // Remove leading '/'
        string fullPath = Path.Combine(Directory.GetCurrentDirectory(), imagePath);

        if (!File.Exists(fullPath)) throw new Exception("File not found");
        else
        {
            byte[] imageBytes = File.ReadAllBytes(fullPath);
            string extension = Path.GetExtension(fullPath).ToLowerInvariant();
            var contentType = ExtensionToContentType[extension] ?? throw new Exception("Please ensure the extension is valid");
            WriteResponse(response, imageBytes, 200, contentType);
        }
    }


    private static async Task<RequestBody> ParseMultipartFormDataAsync (Stream bodyAsBytes, string boundary)
    {
        byte[]? imageBytes = null;
        string? imageExtension = null;
        byte[]? customerIdBytes = null;

        var reader = new MultipartReader(boundary, bodyAsBytes); // The MultipartReader class is used to parse multipart data in an HTTP request
        var section = await reader.ReadNextSectionAsync(); // reads the next "section" from the multipart stream asynchronously

        while (section != null) 
        {
            var contentDisposition = GetContentDisposition(section);
            var sectionName = GetSectionName(contentDisposition); // based on the following example, the name would be customerId, Example Section Header: Content-Disposition: form-data; name="customerId"

            switch (sectionName) 
            {
                case "customerId":
                    customerIdBytes = await GetSectionContentAsync(section);
                    break;
                case "image":
                    imageBytes = await GetSectionContentAsync(section);
                    imageExtension = GetExtension(contentDisposition, section.ContentType);
                    break;
            }

            section = await reader.ReadNextSectionAsync(); // move to the next section
        }

        if (customerIdBytes == null) throw new Exception("Please ensure the customerId is in the request");
        if (imageBytes == null) throw new Exception("Please ensure the image is in the request");
        if (string.IsNullOrEmpty(imageExtension)) throw new Exception($"Please ensure the image extension is one of the following: { string.Join(", ", ImageExtensions) }");

        return new RequestBody { CustomerIdBytes = customerIdBytes, ImageBytes = imageBytes, ImageExtension = imageExtension };
    }


    private class RequestBody
    {
        public required byte[] CustomerIdBytes { get; set; }
        public required byte[] ImageBytes { get; set; }
        public required string ImageExtension { get; set; }
    }


    private static string GetContentDisposition (MultipartSection section)
    {
        if (section.Headers == null) throw new Exception("Please ensure that each section in your request body has a header");

        bool contains = section.Headers.ContainsKey("Content-Disposition");
        int count = section.Headers["Content-Disposition"].Count;

        if (!contains || count != 1) throw new Exception("Please ensure that each section header in your request body has one Content-Disposition, Good Example Section Header: Content-Disposition: form-data; name=\"customerId\"");

        return section.Headers["Content-Disposition"].ToString(); // from list of strings (there is always just one string in the value for Content-Disposition) to a single string (the Content-Type mulit-part section header can have multiple strings, example: application/json, charset=utf-8)
    }



    private static string GetSectionName (string contentDisposition)
    {
        return GetSectionHeaderValue(SectionHeaderKey.Name, contentDisposition, "Please ensure each section header in your request body has a name, Good Example Section Header: Content-Disposition: form-data; name=\"customerId\"");
    }



    private static string? GetExtension (string contentDisposition, string? contentType)
    {
        string? extension = null;

        if (contentType != null)
        {
            if (!ImageContentTypes.Contains(contentType)) throw new Exception($"Please ensure the image is one of the following content types: { string.Join(", ", ImageContentTypes) }");

            string error = "Please ensure each section header in your request body that has a content-type also has a filename, Good Example Section Header: Content-Disposition: form-data; name=\"image\"; filename=\"image.jpg\"";
            string filename = GetSectionHeaderValue(SectionHeaderKey.Filename, contentDisposition, error);

            extension = Path.GetExtension(filename);

            if (string.IsNullOrEmpty(extension)) throw new Exception(error);
            if (!ImageExtensions.Contains(extension)) throw new Exception($"Please ensure the image extension is one of the following: { string.Join(", ", ImageExtensions) }");
        }

        return extension;
    }


    private static string GetSectionHeaderValue (SectionHeaderKey key, string contentDisposition, string error)
    {
        int keyLength;
        string keyValue;

        switch (key)
        {
            case SectionHeaderKey.Name:
                keyLength = 6;
                keyValue = "name=\"";
                break;
            default:
                keyLength = 10;
                keyValue = "filename=\"";
                break;
        }

        int valueKeyIndex = contentDisposition.IndexOf(keyValue, StringComparison.OrdinalIgnoreCase); // find the first index of the string, example: name=", and ignore case 

        if (valueKeyIndex == -1) throw new Exception(error);

        var valueStartIndex = valueKeyIndex + keyLength; // after example: "name=\"" get the index of the name value start
        var valueEndIndex = contentDisposition.IndexOf("\"", valueStartIndex, StringComparison.OrdinalIgnoreCase); // finds the index of the closing quotation mark (") for the example: name value

        if (valueEndIndex == -1) throw new Exception(error);
        
        string value = contentDisposition[valueStartIndex..valueEndIndex]; // substring, from start index to end index

        if (string.IsNullOrEmpty(value)) throw new Exception(error);

        return value;
    }


    private enum SectionHeaderKey { Name, Filename }


    private static async Task<string> SaveImage (RequestBody requestBody)
    {
        var timestamp = DateTime.UtcNow.ToString("o"); // "o" stands for "round-trip" format, which is precise and sortable
        var path = $"{ timestamp }{ requestBody.ImageExtension }";
        var dirPath = Path.Combine("images", path);

        using var inputStream = new MemoryStream(requestBody.ImageBytes); // byte array to memory stream
        using var skbitmap = SKBitmap.Decode(inputStream); // memory stream to SKBitmap object (mutable memory representation of an image)

        if (skbitmap.Width <= MaxImageWidth) await File.WriteAllBytesAsync(dirPath, requestBody.ImageBytes); // if less then or equal to max width => write image
        else
        {
            float aspectRatio = (float)skbitmap.Height / skbitmap.Width;
            int newHeight = (int)(MaxImageWidth * aspectRatio); // calculates the new height for the resized image while maintaining the aspect ratio
            using var resizedSkbitmap = skbitmap.Resize(new SKImageInfo(MaxImageWidth, newHeight), SKSamplingOptions.Default) ?? throw new Exception("Failed to resize image.");
            using var skimage = SKImage.FromBitmap(resizedSkbitmap); // resized bitmap to SKImage object (immutable memory representation of an image)
            using var skdata = skimage.Encode(ExtensionToSkiImageFormat[requestBody.ImageExtension], 100); // SKImage object to SKData object (byte array + image meta data)
            using var outputStream = File.OpenWrite(dirPath); // opens a writable file stream to the specified dirPath
            skdata.SaveTo(outputStream); // saves the image data to the output file stream
        }

        return path;
    }


    private static async Task<byte[]> GetSectionContentAsync (MultipartSection section)
    {
        using var memoryStream = new MemoryStream(); // using ensures that the memoryStream is disposed once the variable is out of scope
        await section.Body.CopyToAsync(memoryStream); // asynchronously copies the content of section.Body to the memoryStream in chunks
        return memoryStream.ToArray(); // converts memoryStream to a byte array
    }


    private static HttpListener StartServer ()
    {
        HttpListener listener = new();
        listener.Prefixes.Add("http://localhost:3000/");
        listener.Start();
        Console.WriteLine("Server is listening on http://localhost:3000/");

        return listener;
    }


    private static async Task<(HttpListenerRequest request, HttpListenerResponse response)> GetContextAsync (HttpListener listener)
    {
        HttpListenerContext context = await listener.GetContextAsync();
        return (request: context.Request, response: context.Response);
    }


    private static void SetResponseHeaders (HttpListenerResponse response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", AllowedOrigin);
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
    }


    private static void OnOptions (HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.OK;  // 200 OK
        response.ContentLength64 = 0;  // no content to send
        response.Close();  // close the response immediately
    }


    private static string GetRequestPath (HttpListenerRequest request)
    {
        if (request.Url == null || (request.Url.AbsolutePath != "/image-upload" && !request.Url.AbsolutePath.StartsWith("/images/"))) throw new Exception("Please call the endpoint /image-upload with a POST or /images with a GET");
        return request.Url.AbsolutePath;
    }


    private static void ValidateImagesRequest (HttpListenerRequest request)
    {
        if (request.HttpMethod != "GET") throw new Exception("Please call with a method of GET");
    }


    private static string ValidateImageUploadRequest (HttpListenerRequest request)
    {
        var contentType = request.Headers["Content-Type"];

        if (request.HttpMethod != "POST") throw new Exception("Please call with a method of POST");
        if (request.Headers["Origin"] != AllowedOrigin) throw new Exception($"Please call from the origin { AllowedOrigin }");
        if (string.IsNullOrEmpty(contentType) || !contentType.Contains("multipart/form-data")) throw new Exception("Please call with a content-type of multipart/form-data");

        return contentType;
    }


    private static void OnError (HttpListenerResponse response, string message)
    {
        var jsonResponse = new { success = false, message };
        WriteResponse(response, JsonToByteArray(jsonResponse), (int)HttpStatusCode.BadRequest);
    }


    private static void OnImageUploadSuccess (HttpListenerResponse response, string filename, string customerId)
    {
        var jsonResponse = new { success = true, filename, customerId };
        WriteResponse(response, JsonToByteArray(jsonResponse));
    }


    private static byte[] JsonToByteArray (object jsonResponse)
    {
        var jsonString = JsonSerializer.Serialize(jsonResponse); // convert json object to a string
        return Encoding.UTF8.GetBytes(jsonString); // convert string to byte array
    }


    private static void WriteResponse (HttpListenerResponse response, byte[] buffer, int statusCode = 200, string contentType = "application/json")
    {
        response.StatusCode = statusCode;
        response.ContentType = contentType;
        response.ContentLength64 = buffer.Length;

        // Opens the output stream for writing data to the client
        // The using statement ensures that after writing the data, the OutputStream is automatically closed (Response.OutputStream.Close()) and disposed of (Resposne.OutputStream.Dispose()), which frees up the associated resources. If an exception is thrown, the using statement ensures the stream is still disposed of correctly.
        // creates a variable output that holds the OutputStream
        using var output = response.OutputStream;
        output.Write(buffer, 0, buffer.Length); // writes data to the OutputStream
    }


    private static readonly string AllowedOrigin = "http://localhost:5174";


    private static readonly int MaxImageWidth = 600;


    private static readonly HashSet<string> ImageContentTypes =
    [
        "image/jpeg", "image/png", "image/webp", "image/bmp"
    ];


    private static readonly HashSet<string> ImageExtensions =
    [
        ".jpg", ".jpeg", ".png", ".webp", ".bmp"
    ];


    private static readonly Dictionary<string, string> ExtensionToContentType = new()
    {
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".webp", "image/webp" },
        { ".bmp", "image/bmp" }
    };


    private static readonly Dictionary<string, SKEncodedImageFormat> ExtensionToSkiImageFormat = new()
    {
        { ".jpg", SKEncodedImageFormat.Jpeg },
        { ".jpeg", SKEncodedImageFormat.Jpeg },
        { ".png", SKEncodedImageFormat.Png },
        { ".webp", SKEncodedImageFormat.Webp },
        { ".bmp", SKEncodedImageFormat.Bmp }
    };
}
