/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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

using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class ResourceMethodsRestorer : MethodCallRestorerBase {
		TypeDefinition getManifestResourceStreamType;
		EmbeddedResource getManifestResourceStreamTypeResource;

		public TypeDefinition Type {
			get { return getManifestResourceStreamType; }
		}

		public Resource Resource {
			get { return getManifestResourceStreamTypeResource; }
		}

		public ResourceMethodsRestorer(ModuleDefinition module)
			: base(module) {
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			foreach (var type in module.Types) {
				if (type.Fields.Count != 1)
					continue;
				if (type.HasNestedTypes || type.HasGenericParameters || type.IsValueType)
					continue;
				if (DotNetUtils.getField(type, "System.Reflection.Assembly") == null)
					continue;
				if (DotNetUtils.getMethod(type, ".cctor") == null)
					continue;

				var getStream2 = getTheOnlyMethod(type, "System.IO.Stream", "(System.Reflection.Assembly,System.Type,System.String)");
				var getNames = getTheOnlyMethod(type, "System.String[]", "(System.Reflection.Assembly)");
				var bitmapCtor = getTheOnlyMethod(type, "System.Drawing.Bitmap", "(System.Type,System.String)");
				var iconCtor = getTheOnlyMethod(type, "System.Drawing.Icon", "(System.Type,System.String)");
				if (getStream2 == null && getNames == null && bitmapCtor == null && iconCtor == null)
					continue;

				var resource = findGetManifestResourceStreamTypeResource(type, simpleDeobfuscator, deob);
				if (resource == null && getStream2 != null)
					continue;

				getManifestResourceStreamType = type;
				createGetManifestResourceStream2(getStream2);
				createGetManifestResourceNames(getNames);
				createBitmapCtor(bitmapCtor);
				createIconCtor(iconCtor);
				getManifestResourceStreamTypeResource = resource;
				break;
			}
		}

		EmbeddedResource findGetManifestResourceStreamTypeResource(TypeDefinition type, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			foreach (var method in type.Methods) {
				if (!method.IsPrivate || !method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.String", "(System.Reflection.Assembly,System.Type,System.String)"))
					continue;
				simpleDeobfuscator.deobfuscate(method);
				simpleDeobfuscator.decryptStrings(method, deob);
				foreach (var s in DotNetUtils.getCodeStrings(method)) {
					var resource = DotNetUtils.getResource(module, s) as EmbeddedResource;
					if (resource != null)
						return resource;
				}
			}
			return null;
		}

		static MethodDefinition getTheOnlyMethod(TypeDefinition type, string returnType, string parameters) {
			MethodDefinition foundMethod = null;

			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null || method.HasGenericParameters)
					continue;
				if (method.IsPrivate)
					continue;
				if (!DotNetUtils.isMethod(method, returnType, parameters))
					continue;

				if (foundMethod != null)
					return null;
				foundMethod = method;
			}

			return foundMethod;
		}
	}
}
