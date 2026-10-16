using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.JellyRoulette.Controllers
{
    [Route("JellyRoulette/Translations")]
    [ApiController]
    [AllowAnonymous] // Permitimos acceso anónimo porque solo devuelve textos estáticos, no datos privados
    public class TranslationsController : ControllerBase
    {
        [HttpGet]
        public IActionResult GetTranslations([FromQuery] string lang = "en")
        {
            // 1. Normalizar: si empieza por "es" usa español, si no, inglés (fallback seguro)
            var normalizedLang = lang.StartsWith("es", System.StringComparison.OrdinalIgnoreCase) ? "es" : "en";
            
            // 2. El nombre del recurso debe coincidir con: [RootNamespace].[Carpeta].[Archivo]
            var resourceName = $"Jellyfin.Plugin.JellyRoulette.translations.{normalizedLang}.json";

            // 3. Leer el archivo incrustado en la DLL
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);
            
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return Content(reader.ReadToEnd(), "application/json; charset=utf-8");
            }

            // 4. Fallback extremo por si acaso (devuelve un JSON vacío)
            return Content("{}", "application/json; charset=utf-8");
        }
    }
}