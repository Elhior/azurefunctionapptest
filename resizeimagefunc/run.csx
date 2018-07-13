#r "Microsoft.WindowsAzure.Storage"
#r "Newtonsoft.Json"
#r "System.Drawing"

using System.Net;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ImageMagick;

public const string connectionString = "DefaultEndpointsProtocol=https;AccountName=dev6u7durr1;AccountKey=QFLqijN/N2cl+fuUeBb4ffn7vp72/Mlyh8OQVWrT2Ly/xyuwsY1bO4TCg9CBe0Qx3LwJ4pLdD84CoXJRO6OgLQ==;EndpointSuffix=core.windows.net";

public static async Task<HttpResponseMessage> Run(HttpRequestMessage req, TraceWriter log)
{
    dynamic requestdata = await req.Content.ReadAsAsync<object>();
    JArray resizeImageParams = requestdata?.ImagesParams;
    string userId = (string)requestdata?.UserId;
    
    List<ResizeImageParams> imagesParams = resizeImageParams.ToObject<List<ResizeImageParams>>();

    //connection to storage    
    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
    CloudBlobClient _blobClient = storageAccount.CreateCloudBlobClient();

    if (imagesParams == null)
        return req.CreateResponse(HttpStatusCode.BadRequest, "Please pass ImagesParams in the request body");

    //trasformation
    foreach (ResizeImageParams imageParams in imagesParams)
    {
        //get blob/container refs
        CloudBlobContainer blolbContainer = _blobClient.GetContainerReference(imageParams.Container);
        CloudBlockBlob sourceImageBlob = blolbContainer.GetBlockBlobReference(imageParams.File);
        CloudBlockBlob resultImageBlob = blolbContainer.GetBlockBlobReference("thumbnails/" + imageParams.File);

        //get image from storage
        MemoryStream sourceImageStream = new MemoryStream();
        await sourceImageBlob.DownloadToStreamAsync(sourceImageStream).ConfigureAwait(false);
        sourceImageStream.Position = 0;
     
        //create and upload resultImage
        using(MemoryStream resultImageStream = new MemoryStream())
        {  
            // Resize the image with the given instructions into the stream.
            Image sourceImage = Image.FromStream(sourceImageStream);
            Image resultImage;
            switch (imageParams.ResizeType)
            {
                case ResizeImageType.Crop:
                    resultImage = CropImage(sourceImage, imageParams.Hight, imageParams.Width);
                    break;
                case ResizeImageType.Fit:
                    resultImage = FitImage(sourceImage, imageParams.Hight, imageParams.Width);
                    break;
                case ResizeImageType.Resize:
                    resultImage = sourceImage.GetThumbnailImage(imageParams.Width, imageParams.Hight, () => false, IntPtr.Zero);
                    break;
                default :
                    resultImage = sourceImage.GetThumbnailImage(imageParams.Width, imageParams.Hight, () => false, IntPtr.Zero);
                    break;
            }            
            resultImage.Save(resultImageStream, ImageFormat.Jpeg);
            resultImageStream.Position = 0;

            ConvertToProgressiveJpeg(resultImageStream);

            await SetBlobMetadataAsync(resultImageBlob, userId);
            // Write the resultImageStream to the new blob.
            await resultImageBlob.UploadFromStreamAsync(resultImageStream);
            await resultImageBlob.SetMetadataAsync().ConfigureAwait(false);
        }
    }

    string result = "TODO";
    return req.CreateResponse(HttpStatusCode.OK, result);
    /*return name == null
        ? req.CreateResponse(HttpStatusCode.BadRequest, "Please pass a name on the query string or in the request body")
        : req.CreateResponse(HttpStatusCode.OK, "Hello " + name);*/
}

private static Image FitImage(Image original, int rectHeight, int rectWidth)
    {
        Image resizedImage;

        if (original.Height == original.Width)
        {
            resizedImage = original.GetThumbnailImage(rectWidth, rectHeight, () => false, IntPtr.Zero);
        }
        else
        {
            //calculate aspect ratio
            float aspect = original.Width / (float)original.Height;
            int newWidth, newHeight;

            //calculate new dimensions based on aspect ratio
            newWidth = (int)(rectWidth * aspect);
            newHeight = rectWidth;

            //if one of the two dimensions exceed the box dimensions
            if (newWidth > rectWidth || newHeight > rectHeight)
            {
                //depending on which of the two exceeds the box dimensions set it as the box dimension and calculate the other one based on the aspect ratio
                if (newWidth > newHeight)
                {
                    newWidth = rectWidth;
                    newHeight = (int)(newWidth / aspect);
                }
                else
                {
                    newHeight = rectHeight;
                    newWidth = (int)(newHeight * aspect);
                }
            }
            resizedImage = original.GetThumbnailImage(newWidth, newHeight, () => false, IntPtr.Zero); 
        }
        return resizedImage;
    }

private static Image CropImage(Image original, int rectHeight, int rectWidth)
{
    int newWidth, newHeight, x, y;

    if (original.Width > original.Height)
    {
        //calculate aspect ratio
        float aspect = original.Width / (float)original.Height;

        //calculate new dimensions based on aspect ratio
        newWidth = (int)(rectWidth * aspect);
        newHeight = rectWidth;
        x = (int)(newWidth - rectWidth) / 2;
        y = 0;
    }
    else
    {
        //calculate aspect ratio
        float aspect = original.Height / (float)original.Width;

        //calculate new dimensions based on aspect ratio
        newHeight = (int)(rectHeight * aspect);
        newWidth = rectHeight;
        x = 0;
        y = (int)(newHeight - rectHeight) / 2;
    }
    var resizedImage = original.GetThumbnailImage(newWidth, newHeight, () => false, IntPtr.Zero);

    Bitmap bmpImage = new Bitmap(resizedImage);

    return bmpImage.Clone(new Rectangle(x, y, rectWidth, rectHeight), bmpImage.PixelFormat);
}

public static void ConvertToProgressiveJpeg(Stream inputImageStream)
{
    using (MagickImage inputMagickImage = new MagickImage(inputImageStream))
    {
        MemoryStream progressiveImageStream = new MemoryStream();
        inputMagickImage.Write(progressiveImageStream, MagickFormat.Pjpeg);
        progressiveImageStream.Position = 0;
        inputImageStream = progressiveImageStream;
        inputImageStream.Position = 0;
    }
}

public static async  Task SetBlobMetadataAsync(CloudBlockBlob blob, string userId)
{
    bool blobExists = await blob.ExistsAsync();

    if (blobExists)
    {
        await blob.FetchAttributesAsync();
        blob.Metadata[FileMetadata.ModifiedDate] = DateTime.UtcNow.ToString();
        blob.Metadata[FileMetadata.ModifiedUserId] = userId;
    }
    else
    {
        blob.Metadata[FileMetadata.CreatedDate] = DateTime.UtcNow.ToString();
        blob.Metadata[FileMetadata.CreatedUserId] = userId;
    }
}
public class ResizeImageParams
{
    public string Container { get; set; }
    public string File { get; set; }
    public ResizeImageType ResizeType { get; set; }
    public int Width { get; set; }
    public int Hight { get; set; }
}

public enum ResizeImageType
{
    Crop = 0,
    Fit = 1,
    Resize = 2
}

public static class FileMetadata
{
    private const string _createdDate = "CreatedDate";
    private const string _createdUserId = "CreatedUserId";
    private const string _version = "Version";
    private const string _basedOnVersion = "BasedOnVersion";
    private const string _modifiedUserId = "ModifiedUserId";
    private const string _modifiedDate = "ModifiedDate";

    public static string CreatedDate => _createdDate;
    public static string CreatedUserId => _createdUserId;
    public static string Version => _version;
    public static string BasedOnVersion => _basedOnVersion;
    public static string ModifiedUserId => _modifiedUserId;
    public static string ModifiedDate => _modifiedDate;
}