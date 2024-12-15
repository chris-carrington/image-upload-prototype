using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;


// A content-type of multipart/form-data indicates that in the request body, each piece of data (form field, file upload, etc) contains its own section
// The boundary is a unique delimiter (example: ------WebKitFormBoundaryECOAm72ObCFGjWIt) that separates different sections in the request body
// When the server processes the request, it uses the boundary (which is provided in the request header content-type) to split the request body into sections
// Example Header => content-type: multipart/form-data; boundary=----WebKitFormBoundaryECOAm72ObCFGjWIt
// Example Request Body:
    // ------WebKitFormBoundaryECOAm72ObCFGjWIt
    // Content-Disposition: form-data; name="image"; filename="image.jpg"
    // Content-Type: image/jpeg
    // <binary content of image.jpg>
    // ------WebKitFormBoundaryECOAm72ObCFGjWIt
    // Content-Disposition: form-data; name="customerId"
    // 123456
    // ------WebKitFormBoundaryECOAm72ObCFGjWIt--
partial class Program
{
    public static async Task Main (string[] args)
    {
        HttpListener listener = new(); // create server
        listener.Prefixes.Add("http://localhost:4000/"); // server will only handle requests that start with this prefix
        listener.Start();  // start server

        Console.WriteLine("C# server is listening on port 4000...");

        while (true) // used to continuously listen for and handle incoming requests (keeps server running)
        {
            HttpListenerContext context = listener.GetContext(); // GetContext() is a blocking call that waits for a new HTTP request. GetContext() prevents further code execution on the current thread until the operation completes. So the thread is "blocked" from doing anything else while it waits for the operation to finish (a request to be received), once a request is recieved GetContext() creates an HttpListenerContext object
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            try
            {
                SetResponseHeaders(response);

                if (request.HttpMethod == "OPTIONS")
                {
                    OnOptions(response);
                    continue;  // makes sure the while(true) loop skips further processing for OPTIONS request, so no additional logic is executed for this type of request.
                }

                var contentType = ValidateRequestMethodOriginAndContentType(request);
                var boundary = GetBoundary(contentType);
                var formData = await ParseMultipartFormDataAsync(request.InputStream, boundary);
                var customerIdAsByteArray = formData["customerId"] ?? throw new Exception("Please ensure the customerId is in the request"); // the null-coalescing operator (??) allows the expression after ?? to be run if the expression before ?? is null
                var imageAsByteArray = formData["image"] ?? throw new Exception("Please ensure the image is in the request");
                var imageAsBase64 = Convert.ToBase64String(imageAsByteArray); // convert byte array to base64
                var customerIdAsString = Encoding.UTF8.GetString(customerIdAsByteArray);

                OnSuccess(response, imageAsBase64, customerIdAsString);
            }
            catch (Exception exception)
            {
                OnError(response, exception.Message);
                continue;
            }
        }
    }


    private static void SetResponseHeaders (HttpListenerResponse response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "http://localhost:5173");
        response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
    }


    private static void OnOptions (HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.OK;  // 200 OK
        response.ContentLength64 = 0;  // no content to send
        response.Close();  // close the response immediately
    }


    private static string ValidateRequestMethodOriginAndContentType (HttpListenerRequest request)
    {
        var origin = request.Headers["Origin"];
        var contentType = request.Headers["Content-Type"];

        if (request.HttpMethod != "POST") throw new Exception("Please call with a method of POST");
        if (origin != "http://localhost:5173") throw new Exception("Please call from the origin http://localhost:5173");
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


    private static async Task<Dictionary<string, byte[]>> ParseMultipartFormDataAsync (Stream requestBody, string boundary)
    {
        var formData = new Dictionary<string, byte[]>();
        var reader = new MultipartReader(boundary, requestBody); // The MultipartReader class is used to parse multipart data in an HTTP request
        var section = await reader.ReadNextSectionAsync(); // reads the next "section" from the multipart stream asynchronously

        while (section != null) 
        {
            var sectionName = GetSectionName(section); // based on the following example, the name would be customerId, Example Section Header: Content-Disposition: form-data; name="customerId"

            if (string.IsNullOrEmpty(sectionName)) throw new Exception("Please ensure each section header in your request body has a name, Good Example Section Header: Content-Disposition: form-data; name=\"customerId\"");

            var content = await ReadSectionContentAsync(section); // Rrad the content of the section as a byte array

            formData[sectionName] = content;
            section = await reader.ReadNextSectionAsync(); // move to the next section
        }

        return formData;
    }


    private static string? GetSectionName (MultipartSection section)
    {
        string? name = null;

        if (section.Headers != null) // Example Section Header: Content-Disposition: form-data; name="customerId"
        {
            bool contains = section.Headers.ContainsKey("Content-Disposition");
            int count = section.Headers["Content-Disposition"].Count;

            if (!contains || count != 1) throw new Exception("Please ensure that each section header in your request body has one Content-Disposition, Good Example Section Header: Content-Disposition: form-data; name=\"customerId\"");

            string contentDisposition = section.Headers["Content-Disposition"].ToString(); // from list of strings (there is always just one string in the value for Content-Disposition) to a single string (the Content-Type mulit-part section header can have multiple strings, example: application/json, charset=utf-8)
            int nameKeyIndex = contentDisposition.IndexOf("name=\"", StringComparison.OrdinalIgnoreCase); // find the first index of the string: name=", and ignore case 

            if (nameKeyIndex != -1)
            {
                var nameValueStartIndex = nameKeyIndex + 6; // after "name=\"" get the index of the name value start
                var nameValueEndIndex = contentDisposition.IndexOf("\"", nameValueStartIndex, StringComparison.OrdinalIgnoreCase); // finds the index of the closing quotation mark (") for the name value

                if (nameValueEndIndex != -1) name = contentDisposition[nameValueStartIndex..nameValueEndIndex]; // substring, from start index to end index
            }
        }

        return name;
    }


    private static async Task<byte[]> ReadSectionContentAsync (MultipartSection section)
    {
        using var memoryStream = new MemoryStream(); // using ensures that the memoryStream is disposed once the variable is out of scope
        await section.Body.CopyToAsync(memoryStream); // asynchronously copies the content of section.Body to the memoryStream in chunks
        return memoryStream.ToArray(); // converts memoryStream to a byte array
    }
}
