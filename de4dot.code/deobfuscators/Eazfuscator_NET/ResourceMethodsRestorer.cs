/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class ResourceMethodsRestorer : MethodCallRestorerBase {
		TypeDef getManifestResourceStreamType;
		EmbeddedResource getManifestResourceStreamTypeResource;

		public TypeDef Type {
			get { return getManifestResourceStreamType; }
		}

		public Resource Resource {
			get { return getManifestResourceStreamTypeResource; }
		}

		public ResourceMethodsRestorer(ModuleDefMD module)
			: base(module) {
		}

		public void Find(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			foreach (var type in module.Types) {
				if (type.Fields.Count != 1)
					continue;
				if (type.HasNestedTypes || type.HasGenericParameters || type.IsValueType)
					continue;
				if (DotNetUtils.GetField(type, "System.Reflection.Assembly") == null)
					continue;
				if (type.FindStaticConstructor() == null)
					continue;

				var getStream2 = GetTheOnlyMethod(type, "System.IO.Stream", "(System.Reflection.Assembly,System.Type,System.String)");
				var getNames = GetTheOnlyMethod(type, "System.String[]", "(System.Reflection.Assembly)");
				var getRefAsms = GetTheOnlyMethod(type, "System.Reflection.AssemblyName[]", "(System.Reflection.Assembly)");
				var bitmapCtor = GetTheOnlyMethod(type, "System.Drawing.Bitmap", "(System.Type,System.String)");
				var iconCtor = GetTheOnlyMethod(type, "System.Drawing.Icon", "(System.Type,System.String)");
				if (getStream2 == null && getNames == null && getRefAsms == null &&
					bitmapCtor == null && iconCtor == null)
					continue;

				var resource = FindGetManifestResourceStreamTypeResource(type, simpleDeobfuscator, deob);
				if (resource == null && getStream2 != null)
					continue;

				getManifestResourceStreamType = type;
				CreateGetManifestResourceStream2(getStream2);
				CreateGetManifestResourceNames(getNames);
				CreateGetReferencedAssemblies(getRefAsms);
				CreateBitmapCtor(bitmapCtor);
				CreateIconCtor(iconCtor);
				getManifestResourceStreamTypeResource = resource;
				break;
			}
		}

		EmbeddedResource FindGetManifestResourceStreamTypeResource(TypeDef type, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			foreach (var method in type.Methods) {
				if (!method.IsPrivate || !method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.String", "(System.Reflection.Assembly,System.Type,System.String)"))
					continue;
				simpleDeobfuscator.Deobfuscate(method);
				simpleDeobfuscator.DecryptStrings(method, deob);
				foreach (var s in DotNetUtils.GetCodeStrings(method)) {
					var resource = DotNetUtils.GetResource(module, s) as EmbeddedResource;
					if (resource != null)
						return resource;
				}
			}
			return null;
		}

		static MethodDef GetTheOnlyMethod(TypeDef type, string returnType, string parameters) {
			MethodDef foundMethod = null;

			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null || method.HasGenericParameters)
					continue;
				if (method.IsPrivate)
					continue;
				if (!DotNetUtils.IsMethod(method, returnType, parameters))
					continue;

				if (foundMethod != null)
					return null;
				foundMethod = method;
			}

			return foundMethod;
		}
	}
}
