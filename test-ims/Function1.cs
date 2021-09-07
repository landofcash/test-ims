using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace test_ims
{
    using System.Collections.Generic;
    using System.Drawing;
    using Ghostscript.NET;
    using Ghostscript.NET.Rasterizer;

    public static class Function1
    {
        [FunctionName("Function1")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req, ILogger log, ExecutionContext context)
        {
            List<string> res = new List<string>();
            try
            {
                var rootApplicationPath = Path.GetFullPath(Path.Combine(context.FunctionDirectory, @"../bin"));
                res.Add($"INFO: rootApplicationPath:{rootApplicationPath}");
                PdfHelper helper = new PdfHelper(log,rootApplicationPath);
                res.Add($"INFO: PdfHelper created.");
                var file = req.Form.Files["file"];
                if (file==null)
                {
                    return new BadRequestObjectResult("File is empty.");
                }
                res.Add($"INFO: File Loaded. Name: {file.FileName} Length: {file.Length.ToString()}");
                using (MemoryStream ms = new MemoryStream())
                {
                    await file.OpenReadStream().CopyToAsync(ms);
                    int pagesTotal = helper.PdfPagesCount(ms);
                    res.Add($"INFO: PagesTotal:{pagesTotal}");
                    var images = await helper.GetPagesBitmapsFromPdf(ms, 1,pagesTotal,200);
                    foreach (Bitmap image in images)
                    {
                        res.Add($"Page image created. Width:{image.Width}, Height:{image.Height}");
                    }
                }
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex);
            }
            return new OkObjectResult(res);
        }
    }

    public class PdfHelper
    {
        protected GhostscriptVersionInfo _version;
        protected ILogger Logger { get; set; }

        public PdfHelper(ILogger logger, string rootApplicationPath)
        {
            Logger = logger;
            LoadNativeAssemblies(rootApplicationPath);
        }

        public void LoadNativeAssemblies(string rootApplicationPath)
        {

            string newPath = Path.GetFullPath(Path.Combine(rootApplicationPath, @"dll/"));
            string gsDllPath = Path.Combine(newPath, Environment.Is64BitProcess ? @"gsdll64.dll" : @"gsdll32.dll");
            
            var folders = "Folders:" + string.Join(";", Directory.GetDirectories(newPath)) + " Files:" + string.Join(";", Directory.GetFiles(newPath));
            Logger.LogInformation($"gsDllPath: {gsDllPath}, Path searched:{folders}");
            if (!File.Exists(gsDllPath))
            {
                folders = "Folders:" + string.Join(";", Directory.GetDirectories(newPath)) + " Files:" + string.Join(";", Directory.GetFiles(newPath));
                Logger.LogError($"The library {gsDllPath} required to run this program is not present! {folders}");
                return;
            }

            try
            {
                _version = new GhostscriptVersionInfo(new Version(0, 0, 0), gsDllPath, string.Empty, GhostscriptLicense.GPL);
            }
            catch(Exception ex)
            {
                Logger.LogError($"Failed to create GhostscriptVersionInfo {ex.Message} {ex.StackTrace}");
            }
        }

        public GhostscriptVersionInfo GetVersion()
        {
            if (_version == null)
            {
                throw new Exception("Error version is null. GhostscriptVersionInfo failed to init.");
            }
            return _version;
        }

        public int PdfPagesCount(Stream pdfStream)
        {
            using (var rasterizer = new GhostscriptRasterizer())
            {
                rasterizer.Open(pdfStream, GetVersion(), true);
                return rasterizer.PageCount;
            }
        }
        

        public async Task<List<Bitmap>> GetPagesBitmapsFromPdf(Stream pdfStream, int startPage, int pagesToTake, int dpi)
        {
            Logger.LogInformation($"GetPagesBitmapsFromPdf: pdfData.length={pdfStream.Length} startPage:{startPage}, pagesToTake:{pagesToTake} ");
            try
            {
                using (var rasterizer = new GhostscriptRasterizer())
                {
                    rasterizer.Open(pdfStream, GetVersion(), true);
                    Logger.LogInformation($"GhostscriptRasterizer.Open: PageCount:{rasterizer.PageCount}");
                    if (rasterizer.PageCount == 0)
                    {
                        Logger.LogError($"GetPagesBitmapsFromPdf No Pages Opened by GhostScriptRasterizer");
                        return new List<Bitmap>();
                    }

                    List<Bitmap> bitmaps = new List<Bitmap>();
                    for (int pageNumber = startPage; pageNumber <= rasterizer.PageCount && pageNumber<startPage+pagesToTake; pageNumber++)
                    {
                        //var image = await Task.Run(() => (Bitmap) rasterizer.GetPage(dpi, pageNumber));
                        var image = (Bitmap) rasterizer.GetPage(dpi, pageNumber);
                        bitmaps.Add(image);
                    }
                    return bitmaps;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error converting PDF to Bitmaps,  Exception: Message:{ex.Message} Stack Trace:{Environment.StackTrace}");
                throw;
            }
        }
    }
}
