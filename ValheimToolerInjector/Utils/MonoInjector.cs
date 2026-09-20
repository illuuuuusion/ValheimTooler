using System;
using System.IO;
using SharpMonoInjector;

namespace ValheimToolerInjector
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string assemblyPath = args.Length > 0
                ? args[0]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ValheimTooler.dll");

            if (!File.Exists(assemblyPath))
            {
                Console.Error.WriteLine("Not found: " + assemblyPath);
                return 1;
            }

            try
            {
                using (var injector = new Injector("valheim"))
                {
                    IntPtr handle = injector.Inject(
                        File.ReadAllBytes(assemblyPath), "ValheimTooler", "Loader", "Init");

                    if (handle == IntPtr.Zero)
                    {
                        Console.Error.WriteLine("Injection returned a null handle.");
                        return 1;
                    }
                }

                Console.WriteLine("Injected. Press Del in game to toggle the window.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
