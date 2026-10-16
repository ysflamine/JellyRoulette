using System.Runtime.InteropServices;

// Jellyfin lee el GUID del plugin desde este atributo del ensamblado
// (BasePlugin<T> lo busca en su constructor). Debe coincidir EXACTAMENTE con
// Plugin.Id y con el "id" del meta.json que genera el csproj al publicar.
//
// Nota: no declaramos aquí [AssemblyProduct]/[AssemblyCompany]/[AssemblyCopyright]
// porque el SDK de .NET ya los genera a partir de las propiedades del csproj, y
// duplicarlos provoca el error CS0579.
[assembly: Guid("f299f82b-8485-4547-be7c-14e0e10e3b2c")]
