﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ComponentModel;
using Jacere.Core.Attributes;
using Jacere.Core.Util;

namespace Jacere.Core
{
	public enum AssemblyLoadResult
	{

	}

	public static class ExtensionManager
	{
		private static readonly string c_baseDirectory;
		private static readonly Assembly[] c_localAssemblies;
		private static readonly Type[] c_localTypes;

		static ExtensionManager()
		{
			c_baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

			c_localAssemblies = LoadAssemblies();
			c_localTypes = c_localAssemblies.GetTypes();
		}

		#region Discovery

		private static Assembly[] LoadAssemblies()
		{
			// this could be customized in the future to specify subdirectories/filters to search
			var files = Directory.GetFiles(c_baseDirectory, "*.dll", SearchOption.AllDirectories);
			var localFileMap = files.ToDictionary(Path.GetFileNameWithoutExtension, f => f, StringComparer.OrdinalIgnoreCase);
			
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			var localAssemblyMap = assemblies
				.Where(a => !string.IsNullOrEmpty(a.Location))
				.Where(a => a.Location.StartsWith(c_baseDirectory, StringComparison.OrdinalIgnoreCase))
				.ToDictionary(a => a.FullName, a => a);

			var productExtensions = new Dictionary<string, HashSet<Assembly>>();
			var assembliesToCheck = new Stack<string>(localAssemblyMap.Keys);

			while (assembliesToCheck.Count > 0)
			{
				var assemblyFullName = assembliesToCheck.Pop();
				Assembly assembly = null;
				if (!localAssemblyMap.ContainsKey(assemblyFullName))
				{
					// load it
					var assemblyName = new AssemblyName(assemblyFullName);
					try
					{
						var assemblyPath = localFileMap[assemblyName.Name];
						var header = PEHeader.Load(assemblyPath);
						if (header.IsManaged)
						{
							// figure a way to check this properly in the future
							// (taking into account "Any CPU", etc.)
							if (header.Is64Bit == Environment.Is64BitProcess)
							{
								assembly = Assembly.Load(assemblyName);
							}
						}
					}
					catch
					{
					}
#warning I don't want to add assemblies that aren't marked as an extension
					localAssemblyMap.Add(assemblyFullName, assembly);
				}

				assembly = localAssemblyMap[assemblyFullName];
				if (assembly != null)
				{
					var extensionAttribute = assembly.GetExtensionAttribute();
					if (extensionAttribute != null)
					{
						// add it to the extension list
						if (!productExtensions.ContainsKey(extensionAttribute.ProductName))
							productExtensions.Add(extensionAttribute.ProductName, new HashSet<Assembly>());
						productExtensions[extensionAttribute.ProductName].Add(assembly);

						// identify local references and add to queue
						var referencedAssemblies = assembly.GetReferencedAssemblies();
						foreach (var a in referencedAssemblies.Where(a => localFileMap.ContainsKey(a.Name)))
							assembliesToCheck.Push(a.FullName);
					}
				}
			}

			return localAssemblyMap.Values.ToArray();
		}

		///// <summary>
		///// Currently, this looks at all directories below the current domain base, recursively.
		///// </summary>
		//private static void RegisterExtensions()
		//{
		//    ContextManager.WriteLine("[Extensions]");

		//    var assemblyLookup = AppDomain.CurrentDomain.GetAssemblyLocationLookup();

		//    String path = c_baseDirectory;
		//    if (Directory.Exists(path))
		//    {
		//        var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);

		//        var newAssemblyPaths = files
		//            .Where(f => Path.GetExtension(f) == ".dll" && !assemblyLookup.ContainsKey(f));

		//        foreach (String assemblyPath in newAssemblyPaths)
		//        {
		//            char result = '-';
		//            try
		//            {
		//                PEHeader header = PEHeader.Load(assemblyPath);
		//                if (header.IsManaged)
		//                {
		//                    // figure a way to check this properly in the future
		//                    // (taking into account "Any CPU", etc.)
		//                    if (header.Is64Bit == Environment.Is64BitProcess)
		//                    {
		//                        AssemblyName assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
		//                        Assembly assembly = Assembly.Load(assemblyName);
		//                        assemblyLookup.Add(assemblyPath, assembly);
		//                        result = '+';
		//                    }
		//                }
		//            }
		//            catch (Exception)
		//            {
		//                result = 'x';
		//            }
		//            ContextManager.WriteLine(" {0} {1}", result, Path.GetFileNameWithoutExtension(assemblyPath));
		//        }
		//    }
		//}

		//private static void RegisterFactories()
		//{
		//    ProcessLoadedTypesInitialize("Factories", typeof(IFactory));
		//}

		//private static void RegisterProperties()
		//{
		//    ProcessLoadedTypesInitialize("Properties", typeof(IPropertyContainer));
		//}

		//private static void ProcessLoadedTypesInitialize(string processName, Type baseType)
		//{
		//    ProcessLoadedTypes(
		//        0,
		//        processName,
		//        baseType.IsAssignableFrom,
		//        t => !t.IsAbstract,
		//        t => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(t.TypeHandle)
		//    );
		//}

		//public static void ProcessLoadedTypes(int level, string processName, Func<Type, bool> consider, Func<Type, bool> attempt, Action<Type> action)
		//{
		//    string padding = "".PadRight(level * 2);

		//    WriteLine("{0}[{1}]", padding, processName);

		//    var types = GetLoadedTypes(consider);
		//    foreach (Type type in types)
		//    {
		//        char result = '-';
		//        if (attempt(type))
		//        {
		//            try
		//            {
		//                action(type);
		//                result = '+';
		//            }
		//            catch (Exception)
		//            {
		//                result = 'x';
		//            }
		//        }

		//        if (Config.ShowAbstractTypesDuringDiscovery || result != '-')
		//            WriteLine("{0} {1} {2}", padding, result, type.Name);
		//    }
		//}

		#endregion
	}
}
