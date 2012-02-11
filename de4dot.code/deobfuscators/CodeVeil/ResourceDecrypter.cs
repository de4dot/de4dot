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

using System;
using System.IO;
using Mono.Cecil;

namespace de4dot.code.deobfuscators.CodeVeil {
	class ResourceDecrypter {
		ModuleDefinition module;

		public ResourceDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void decrypt() {
			for (int i = 0; i < module.Resources.Count; i++) {
				var resource = module.Resources[i] as EmbeddedResource;
				if (resource == null)
					continue;

				var decrypted = decrypt(resource.GetResourceStream());
				if (decrypted == null)
					continue;

				module.Resources[i] = new EmbeddedResource(resource.Name, resource.Attributes, decrypted);
			}
		}

		byte[] decrypt(Stream stream) {
			try {
				stream.Position = 0;
				var reader = new BinaryReader(stream);
				uint sig = reader.ReadUInt32();
				if (sig == 0xBEEFCACE)
					return decryptBeefcace(reader);
				//TODO: Decrypt the other type
				return null;
			}
			catch (ApplicationException) {
				return null;
			}
			catch (Exception ex) {
				Log.w("Got an exception when decrypting resources: {0}", ex.GetType());
				return null;
			}
		}

		byte[] decryptBeefcace(BinaryReader reader) {
			var resourceReader = new ResourceReader(reader);
			return new ResourceConverter(module, resourceReader.read()).convert();
		}
	}
}
