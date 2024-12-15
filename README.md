# Image Upload Prototype

### How to run in development
1. In bash run `cd react && npm run dev`
    - If this is your first time running, in the `react` folder:
        - In bash run `nvm use 22`
        - Add & populate the `.env.development` and `.env.production`file
1. In bash run `cd csharp && dotnet run`
    - If this is your first time running, in the `csharp` folder, in bash run `dotnet add package Microsoft.AspNetCore.WebUtilities`


### How to run in production:
1. In bash run `cd react && npm run build` so that a `dist` folder is created and populated
1. Upload the  the `js` and `css` files in `react/dist/assets` to the demo server
1. Navigate to the aspx page that will hold the image upload component
1. Wherever you'd love the image upload component to function add this: `<div id="image-upload"></div>`
1. In the `<head>` add a `<link>` tag for the dist css file
    * Example: `<link rel="stylesheet" href="image-upload.css">`
1. Just below the newly added `<link>` tag add a `<script>` tag for the dist js file
    * Example: `<script src="image-upload.js" type="module"></script>`
1. Ensure the tag has `type="module"` so:
    * The browser continues parsing the HTML content while fetching and processing the module script
    * The script is executed only after the document's body has been fully parsed
    * Variables, functions, and classes declared in a module are scoped to that module and do not pollute the global namespace
    * Module scripts are automatically executed in strict mode, which enforces stricter parsing and error handling of JavaScript code
    * Supports ES6 module syntax like `import` & `export`