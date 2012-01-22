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

namespace de4dot.code.deobfuscators.DeepSea {
	class ResourceResolver : ResolverBase {
		EmbeddedResource resource;

		public ResourceResolver(ModuleDefinition module)
			: base(module) {
		}

		static string[] handlerLocalTypes = new string[] {
			"System.AppDomain",
			"System.Byte[]",
			"System.Collections.Generic.Dictionary`2<System.String,System.String>",
			"System.IO.Compression.DeflateStream",
			"System.IO.MemoryStream",
			"System.IO.Stream",
			"System.Reflection.Assembly",
			"System.String",
			"System.String[]",
		};
		protected override bool checkHandlerMethodInternal(MethodDefinition handler) {
			return new LocalTypes(handler).all(handlerLocalTypes);
		}

		public void initialize(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (resolveHandler == null)
				return;

			simpleDeobfuscator.deobfuscate(resolveHandler);
			simpleDeobfuscator.decryptStrings(resolveHandler, deob);
			resource = DeobUtils.getEmbeddedResourceFromCodeStrings(module, resolveHandler);
			if (resource == null) {
				Log.w("Could not find resource of encrypted resources");
				return;
			}
		}

		public EmbeddedResource mergeResources() {
			if (resource == null)
				return null;

			DeobUtils.decryptAndAddResources(module, resource.Name, () => decryptResource(resource));
			return resource;
		}
	}
}
