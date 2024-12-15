import './image-upload.css'
import React, { useState } from 'react'


export default function ImageUpload (): React.ReactElement {
  const [ file, setFile ] = useState<File | null>(null)
  const [ feImageSrc, setFeImageSrc ] = useState<string | null>(null)
  const [ beImageSrc, setBeImageSrc ] = useState<string | null>(null)

  return (
    <>
      <div className="form">
        <input type="file" accept="image/*" onChange={ e => onFileChange(e, setFile, setFeImageSrc, setBeImageSrc) } /> { /* b/c "accept" is "image/*" this file uploader will only accept images & the camera option */ }
        <button type="button" onClick={ () => uploadImage(file, setBeImageSrc) }>Upload</button>
      </div>

      <div className="images">
        {( feImageSrc && 
          <div className="img">
            <div className="title">Frontend Preview:</div>
            { feImageSrc && <img src={ feImageSrc } alt="feImageSrc" /> }
          </div>
        )}

        {( beImageSrc &&
          <div className="img">
            <div className="title">Backend Response:</div>
            { beImageSrc && <img src={`data:image/png;base64,${ beImageSrc }`}  alt="beImageSrc" /> }
          </div>
        )}
      </div>
    </>
  )
}


async function onFileChange (event: React.ChangeEvent<HTMLInputElement>, setFile: React.Dispatch<React.SetStateAction<File | null>>, setFeImageSrc: React.Dispatch<React.SetStateAction<string | null>>, setBeImageSrc: React.Dispatch<React.SetStateAction<string | null>>): Promise<void> {
  const file = event.target.files?.[0]

  viewImage(file, setFeImageSrc)
  setFile(file || null)
}


function viewImage (file: File | undefined, setFeImageSrc: React.Dispatch<React.SetStateAction<string | null>>) {
  if (file) {
    const reader = new FileReader() // lets web applications asynchronously read the contents of files

    reader.onload = () => {
      if (typeof reader.result === 'string') setFeImageSrc(reader.result) // could be type ArrayBuffer if reader.readAsArrayBuffer(file) or null if there was an error
    }

    reader.onerror = () => {
      console.error('Error reading file:', reader.error?.message)
    }

    reader.readAsDataURL(file) // reads the contents of the provided file as base64
  }
}


async function uploadImage (file: File | null, setBeImageSrc: React.Dispatch<React.SetStateAction<string | null>>) {
  try {
    if (file) {
      const formData = new FormData()
      formData.append('image', file)
      formData.append('customerId', "123456")
  
      const response = await fetch(import.meta.env.VITE_API_URL, { method: 'POST', body: formData })
      const result = await response.json()

      setBeImageSrc(result.base64)
    }
  } catch (error) {
    console.error('Error uploading the file:', error);
  }
}
