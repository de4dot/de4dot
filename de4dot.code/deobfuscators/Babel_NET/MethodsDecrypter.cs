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
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class MethodsDecrypter {
		ModuleDefinition module;
		ResourceDecrypter resourceDecrypter;
		IDeobfuscatorContext deobfuscatorContext;
		Dictionary<string, ImageReader> imageReaders = new Dictionary<string, ImageReader>(StringComparer.Ordinal);
		TypeDefinition methodsDecrypterCreator;
		TypeDefinition methodsDecrypter;
		MethodDefinition decryptExecuteMethod;
		EmbeddedResource encryptedResource;

		public bool Detected {
			get { return methodsDecrypterCreator != null; }
		}

		public MethodsDecrypter(ModuleDefinition module, ResourceDecrypter resourceDecrypter, IDeobfuscatorContext deobfuscatorContext) {
			this.module = module;
			this.resourceDecrypter = resourceDecrypter;
			this.deobfuscatorContext = deobfuscatorContext;
		}

		public void find() {
			var requiredFields = new string[] {
				"System.Threading.ReaderWriterLock",
				"System.Collections.Hashtable",
			};
			foreach (var type in module.GetTypes()) {
				var fieldTypes = new FieldTypes(type);
				if (!fieldTypes.all(requiredFields))
					continue;
				if (DotNetUtils.getMethod(type, "Finalize") == null)
					continue;
				var executeMethod = DotNetUtils.getMethod(type, "System.Object", "(System.String,System.Object[])");
				if (executeMethod == null || !executeMethod.IsStatic || executeMethod.Body == null)
					continue;

				var decrypterType = findMethodsDecrypterType(type);
				if (decrypterType == null)
					continue;

				resourceDecrypter.DecryptMethod = findDecryptMethod(decrypterType);

				methodsDecrypterCreator = type;
				methodsDecrypter = decrypterType;
				decryptExecuteMethod = executeMethod;
				return;
			}
		}

		static MethodDefinition findDecryptMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				var decryptMethod = ResourceDecrypter.findDecrypterMethod(method);
				if (decryptMethod != null)
					return decryptMethod;
			}
			return null;
		}

		TypeDefinition findMethodsDecrypterType(TypeDefinition type) {
			foreach (var field in type.Fields) {
				var fieldType = DotNetUtils.getType(module, field.FieldType);
				if (fieldType == null)
					continue;
				if (DotNetUtils.getMethod(fieldType, "Finalize") == null)
					continue;
				if (!new FieldTypes(fieldType).exists("System.Collections.Hashtable"))
					continue;
				if (DotNetUtils.getMethod(fieldType, "System.String", "()") == null)
					continue;

				return fieldType;
			}

			return null;
		}

		public void initialize(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (methodsDecrypter == null)
				return;

			encryptedResource = BabelUtils.findEmbeddedResource(module, methodsDecrypter, simpleDeobfuscator, deob);
			if (encryptedResource != null)
				addImageReader("", resourceDecrypter.decrypt(encryptedResource.GetResourceData()));
		}

		ImageReader addImageReader(string name, byte[] data) {
			var imageReader = new ImageReader(deobfuscatorContext, module, data);
			if (!imageReader.initialize()) {
				Log.w("Could not read encrypted methods");
				return null;
			}
			if (imageReaders.ContainsKey(name))
				throw new ApplicationException(string.Format("ImageReader for name '{0}' already exists", name));
			imageReaders[name] = imageReader;
			return imageReader;
		}

		class EncryptInfo {
			public string encryptedMethodName;
			public string feature;
			public MethodDefinition method;

			public string FullName {
				get {
					if (string.IsNullOrEmpty(feature))
						return encryptedMethodName;
					return string.Format("{0}:{1}", feature, encryptedMethodName);
				}
			}

			public EncryptInfo(string encryptedMethodName, string feature, MethodDefinition method) {
				this.encryptedMethodName = encryptedMethodName;
				this.feature = feature;
				this.method = method;
			}

			public override string ToString() {
				if (feature != "")
					return string.Format("{0}:{1} {2:X8}", feature, encryptedMethodName, method.MetadataToken.ToInt32());
				else
					return string.Format("{0} {1:X8}", encryptedMethodName, method.MetadataToken.ToInt32());
			}
		}

		public void decrypt() {
			int numNonDecryptedMethods = 0;
			int totalEncryptedMethods = 0;
			foreach (var info in getEncryptedMethods()) {
				totalEncryptedMethods++;
				var imageReader = getImageReader(info.feature);
				if (imageReader == null) {
					numNonDecryptedMethods++;
					continue;
				}
				Log.v("Decrypting method {0:X8}", info.method.MetadataToken.ToInt32());
				imageReader.restore(info.FullName, info.method);
			}
			if (numNonDecryptedMethods > 0)
				Log.w("{0}/{1} methods not decrypted", numNonDecryptedMethods, totalEncryptedMethods);
		}

		ImageReader getImageReader(string feature) {
			ImageReader imageReader;
			if (imageReaders.TryGetValue(feature, out imageReader))
				return imageReader;

			return createImageReader(feature);
		}

		ImageReader createImageReader(string feature) {
			if (string.IsNullOrEmpty(feature))
				return null;

			try {
				var encrypted = File.ReadAllBytes(getFile(Path.GetDirectoryName(module.FullyQualifiedName), feature));
				var decrypted = resourceDecrypter.decrypt(encrypted);
				return addImageReader(feature, decrypted);
			}
			catch {
				return null;
			}
		}

		static string getFile(string dir, string name) {
			try {
				var di = new DirectoryInfo(dir);
				foreach (var file in di.GetFiles()) {
					if (Utils.StartsWith(file.Name, name, StringComparison.OrdinalIgnoreCase))
						return file.FullName;
				}
			}
			catch {
			}
			return null;
		}

		List<EncryptInfo> getEncryptedMethods() {
			var infos = new List<EncryptInfo>();

			foreach (var type in module.GetTypes()) {
				foreach (var method in type.Methods) {
					EncryptInfo info;
					if (checkEncryptedMethod(method, out info))
						infos.Add(info);
				}
			}

			return infos;
		}

		bool checkEncryptedMethod(MethodDefinition method, out EncryptInfo info) {
			info = null;
			if (method.Body == null)
				return false;
			if (!callsExecuteMethod(method))
				return false;

			var strings = DotNetUtils.getCodeStrings(method);
			if (strings.Count != 1)
				throw new ApplicationException(string.Format("Could not find name of encrypted method"));

			string feature = "";
			string name = strings[0];
			int index = name.IndexOf(':');
			if (index >= 0) {
				feature = name.Substring(0, index);
				name = name.Substring(index + 1);
			}

			info = new EncryptInfo(name, feature, method);
			return true;
		}

		bool callsExecuteMethod(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
					continue;
				if (MemberReferenceHelper.compareMethodReferenceAndDeclaringType(decryptExecuteMethod, instr.Operand as MethodReference))
					return true;
			}
			return false;
		}
	}
}
