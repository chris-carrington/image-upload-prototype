import fs from 'node:fs'
import multiparty from 'multiparty'
import http, { type ServerResponse } from 'node:http'


const server = http.createServer((req, res) => { // create an HTTP server
  setResponseHeaders(res)

  if (req.url !== '/image-upload') return onError(res, 'Please ensure the url is /image-upload')
  if (req.method === 'OPTIONS') return onOptions(res)
  if (req.method !== 'POST') return onError(res, 'Please call with a method of POST')
  if (req.headers['origin'] !== 'http://localhost:5173') return onError(res, 'Please call from the origin http://localhost:5173')
  if (req.headers['content-type'] && !req.headers['content-type'].includes('multipart/form-data')) return onError(res, 'Please call with a content-type of multipart/form-data')

  const form = new multiparty.Form() // create a multiparty form parser

  form.parse(req, (err, _, files) => { // parse form data
    if (err) return onError(res, 'Error processing the form')
    if (files.image?.length !== 1) return onError(res, 'Please upload 1 image')

    const base64 = getBase64(files.image[0])
    onSuccess(res, base64)
  })
})


server.listen(3000, () => { // start the HTTP server
  console.log('Node server is listening on port 3000...')
})


function setResponseHeaders (res: ServerResponse): void {
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type')
  res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS')
  res.setHeader('Access-Control-Allow-Origin', 'http://localhost:5173')
}


function onOptions (res: ServerResponse): void {
  res.writeHead(200)
  res.end()
}


function onError (res: ServerResponse, message: string): void {
  res.writeHead(400, { 'Content-Type': 'application/json' })
  res.end(JSON.stringify({ success: false, error: message }))
}


function onSuccess (res: ServerResponse, base64: string): void {
  res.writeHead(200, { 'Content-Type': 'application/json' })
  res.end(JSON.stringify({ success: true, base64 }))
}


function getBase64 (image: multiparty.File): string {
  const fileBuffer = fs.readFileSync(image.path)
  return fileBuffer.toString('base64')
}
