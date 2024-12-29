using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;


partial class Program
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

                var contentType = ValidateRequest(request);
                var boundary = GetBoundary(contentType);
                var formData = await ParseMultipartFormDataAsync(request.InputStream, boundary);
                var customerIdFormDataValue = formData["customerId"] ?? throw new Exception("Please ensure the customerId is in the request"); // the null-coalescing operator (??) allows the expression after ?? to be run if the expression before ?? is null
                var imageFormDataValue = formData["image"] ?? throw new Exception("Please ensure the image is in the request");
                var imageAsBase64 = Convert.ToBase64String(imageFormDataValue.Bytes); // convert byte array to base64
                var customerIdAsString = Encoding.UTF8.GetString(customerIdFormDataValue.Bytes);
                Console.WriteLine(imageFormDataValue.Extension);

                OnSuccess(response, imageAsBase64, customerIdAsString);
            }
            catch (Exception exception)
            {
                OnError(response, exception.Message);
                continue;
            }
        }
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
        response.Headers.Add("Access-Control-Allow-Origin", "http://localhost:5174");
        response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
    }


    private static void OnOptions (HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.OK;  // 200 OK
        response.ContentLength64 = 0;  // no content to send
        response.Close();  // close the response immediately
    }


    private static string ValidateRequest (HttpListenerRequest request)
    {
        var origin = request.Headers["Origin"];
        var contentType = request.Headers["Content-Type"];

        if (request.HttpMethod != "POST") throw new Exception("Please call with a method of POST");
        if (origin != "http://localhost:5174") throw new Exception("Please call from the origin http://localhost:5174");
        if (request.Url == null || request.Url.AbsolutePath != "/image-upload") throw new Exception("Please call the endpoint /image-upload");
        if (string.IsNullOrEmpty(contentType) || !contentType.Contains("multipart/form-data")) throw new Exception("Please call with a content-type of multipart/form-data");

        return contentType;
    }


    private static void OnError (HttpListenerResponse response, string message)
    {
        var jsonResponse = new { success = false, message };
        WriteResponse(response, JsonToByteArray(jsonResponse), (int)HttpStatusCode.BadRequest);
    }


    private static void OnSuccess (HttpListenerResponse response, string base64, string customerId)
    {
        var jsonResponse = new { success = true, base64, customerId };
        WriteResponse(response, JsonToByteArray(jsonResponse));
    }


    private static byte[] JsonToByteArray (object jsonResponse)
    {
        var jsonString = JsonSerializer.Serialize(jsonResponse); // convert json object to a string
        return Encoding.UTF8.GetBytes(jsonString); // convert string to byte array
    }


    private static void WriteResponse (HttpListenerResponse response, byte[] buffer, int statusCode = 200)
    {
        response.ContentLength64 = buffer.Length;
        response.ContentType = "application/json";
        response.StatusCode = statusCode;

        // Opens the output stream for writing data to the client
        // The using statement ensures that after writing the data, the OutputStream is automatically closed (Response.OutputStream.Close()) and disposed of (Resposne.OutputStream.Dispose()), which frees up the associated resources. If an exception is thrown, the using statement ensures the stream is still disposed of correctly.
        // creates a variable output that holds the OutputStream
        using var output = response.OutputStream;
        output.Write(buffer, 0, buffer.Length); // writes data to the OutputStream
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


    private static async Task<Dictionary<string, FormDataValue>> ParseMultipartFormDataAsync (Stream requestBody, string boundary)
    {
        var formData = new Dictionary<string, FormDataValue>();
        var reader = new MultipartReader(boundary, requestBody); // The MultipartReader class is used to parse multipart data in an HTTP request
        var section = await reader.ReadNextSectionAsync(); // reads the next "section" from the multipart stream asynchronously

        while (section != null) 
        {
            var contentDisposition = GetContentDisposition(section);
            var extension = GetExtension(contentDisposition, section.ContentType);
            var sectionName = GetSectionName(contentDisposition); // based on the following example, the name would be customerId, Example Section Header: Content-Disposition: form-data; name="customerId"
            var content = await GetSectionContentAsync(section); // Rrad the content of the section as a byte array

            formData[sectionName] = new FormDataValue { Bytes = content, Extension = extension };
            section = await reader.ReadNextSectionAsync(); // move to the next section
        }

        return formData;
    }


    private class FormDataValue
    {
        public required byte[] Bytes { get; set; }
        public string? Extension { get; set; }
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
        string error = "Please ensure each section header in your request body has a name, Good Example Section Header: Content-Disposition: form-data; name=\"customerId\"";
        int nameKeyIndex = contentDisposition.IndexOf("name=\"", StringComparison.OrdinalIgnoreCase); // find the first index of the string: name=", and ignore case 

        if (nameKeyIndex == -1) throw new Exception(error);

        var nameValueStartIndex = nameKeyIndex + 6; // after "name=\"" get the index of the name value start
        var nameValueEndIndex = contentDisposition.IndexOf("\"", nameValueStartIndex, StringComparison.OrdinalIgnoreCase); // finds the index of the closing quotation mark (") for the name value

        if (nameValueEndIndex == -1) throw new Exception(error);
        
        string name = contentDisposition[nameValueStartIndex..nameValueEndIndex]; // substring, from start index to end index

        if (string.IsNullOrEmpty(name)) throw new Exception(error);

        return name;
    }



    private static string? GetExtension (string contentDisposition, string? contentType)
    {
        string? extension = null;
        var imageContentTypes = new HashSet<string> { "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp" };

        if (contentType != null && imageContentTypes.Contains(contentType))
        {
            string error = "Please ensure each section header in your request body that has a content-type also has a filename, Good Example Section Header: Content-Disposition: form-data; name=\"image\"; filename=\"image.jpg\"";
            int filenameKeyIndex = contentDisposition.IndexOf("filename=\"", StringComparison.OrdinalIgnoreCase); // find the first index of the string: name=", and ignore case 

            if (filenameKeyIndex == -1) throw new Exception(error);

            var filenameValueStartIndex = filenameKeyIndex + 10; // after "filename=\"" get the index of the filename value start
            var filenameValueEndIndex = contentDisposition.IndexOf("\"", filenameValueStartIndex, StringComparison.OrdinalIgnoreCase); // finds the index of the closing quotation mark (") for the filename value

            if (filenameValueEndIndex == -1) throw new Exception(error);

            var filename = contentDisposition[filenameValueStartIndex..filenameValueEndIndex]; // substring, from start index to end index

            if (string.IsNullOrEmpty(filename)) throw new Exception(error);

            extension = Path.GetExtension(filename);

            if (string.IsNullOrEmpty(extension)) throw new Exception(error);

            var imageExtensions = new HashSet<string> { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

            if (!imageExtensions.Contains(extension)) throw new Exception("Please ensue that the image extension is one of the following: '.jpg', '.jpeg', '.png', '.gif', '.webp' or '.bmp'");
        }

        return extension;
    }


    private static async Task<byte[]> GetSectionContentAsync (MultipartSection section)
    {
        using var memoryStream = new MemoryStream(); // using ensures that the memoryStream is disposed once the variable is out of scope
        await section.Body.CopyToAsync(memoryStream); // asynchronously copies the content of section.Body to the memoryStream in chunks
        return memoryStream.ToArray(); // converts memoryStream to a byte array
    }
}
