// Loads the launcher's bundled managed libraries directly from the executable.
using System;
using System.IO;
using System.Reflection;

namespace AzerothLauncher
{
    internal static class EmbeddedLibraries
    {
        static bool registered;

        public static void Register()
        {
            if (registered) return;
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            registered = true;
        }

        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string simpleName;
            try { simpleName = new AssemblyName(args.Name).Name; }
            catch (Exception) { return null; }
            if (string.IsNullOrEmpty(simpleName)) return null;

            Assembly owner = typeof(EmbeddedLibraries).Assembly;
            string resource = "AS.extractor." + simpleName + ".dll";
            using (Stream input = owner.GetManifestResourceStream(resource))
            {
                if (input == null) return null;
                using (var output = new MemoryStream())
                {
                    input.CopyTo(output);
                    return Assembly.Load(output.ToArray());
                }
            }
        }
    }
}
