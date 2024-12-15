import { StrictMode } from 'react'
import ImageUpload from './ImageUpload.tsx'
import { createRoot } from 'react-dom/client'


const element = document.getElementById('image-upload')


if (element) {
  // initializes React's rendering system and connects it to the specified DOM element
  const initialized = createRoot(element)


  // .render(): renders the provided React components into the DOM
  // StrictMode: activates additional checks and warnings in development mode. It does not affect production builds
  initialized.render( 
    <StrictMode> 
      <ImageUpload />
    </StrictMode>
  )
}
