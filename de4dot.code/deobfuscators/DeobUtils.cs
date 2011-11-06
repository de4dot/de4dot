using System;
using System.IO;
using Mono.Cecil;

namespace de4dot.deobfuscators {
	static class DeobUtils {
		public static void decryptAndAddResources(ModuleDefinition module, string encryptedName, Func<byte[]> decryptResource) {
			Log.v("Decrypting resources, name: {0}", Utils.toCsharpString(encryptedName));
			var decryptedResourceData = decryptResource();
			if (decryptedResourceData == null)
				throw new ApplicationException("decryptedResourceData is null");
			var resourceModule = ModuleDefinition.ReadModule(new MemoryStream(decryptedResourceData));

			Log.indent();
			foreach (var rsrc in resourceModule.Resources) {
				Log.v("Adding decrypted resource {0}", Utils.toCsharpString(rsrc.Name));
				module.Resources.Add(rsrc);
			}
			Log.deIndent();
		}

		public static T lookup<T>(ModuleDefinition module, T def, string errorMessage) where T : MemberReference {
			if (def == null)
				return null;
			var newDef = module.LookupToken(def.MetadataToken.ToInt32()) as T;
			if (newDef == null)
				throw new ApplicationException(errorMessage);
			return newDef;
		}
	}
}
