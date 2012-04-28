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
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeVeil {
	class ResourceDecrypter {
		ModuleDefinition module;
		TypeDefinition encryptedResourceStreamType;
		TypeDefinition encryptedResourceSetType;
		MethodDefinition encryptedResourceSet_GetDefaultReader;
		TypeDefinition encryptedResourceReaderType;
		GenericInstanceType encryptedResourceReaderTypeDict;
		TypeDefinition resType;
		MethodDefinition resTypeCtor;
		TypeDefinition resourceFlagsType;
		TypeDefinition resourceEnumeratorType;
		MethodCallRestorerBase methodsRestorer;

		public bool CanRemoveTypes {
			get {
				return EncryptedResourceStreamType != null &&
					EncryptedResourceSetType != null &&
					EncryptedResourceReaderType != null &&
					ResType != null &&
					ResourceFlagsType != null &&
					ResourceEnumeratorType != null;
			}
		}

		public TypeDefinition EncryptedResourceStreamType {
			get { return encryptedResourceStreamType; }
		}

		public TypeDefinition EncryptedResourceSetType {
			get { return encryptedResourceSetType; }
		}

		public TypeDefinition EncryptedResourceReaderType {
			get { return encryptedResourceReaderType; }
		}

		public TypeDefinition ResType {
			get { return resType; }
		}

		public TypeDefinition ResourceFlagsType {
			get { return resourceFlagsType; }
		}

		public TypeDefinition ResourceEnumeratorType {
			get { return resourceEnumeratorType; }
		}

		public ResourceDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void initialize() {
			methodsRestorer = new MethodCallRestorerBase(module);
			findEncryptedResourceStreamType();
			findEncryptedResourceSet();
			findEncryptedResourceReader();
			findResType();
			findResourceFlags();
			findResourceEnumerator();
		}

		void findResourceEnumerator() {
			if (encryptedResourceReaderType == null)
				return;

			var resourceEnumeratorType_fields = new string[] {
				"System.Collections.DictionaryEntry",
				"System.Collections.IDictionaryEnumerator",
				"System.Boolean",
				encryptedResourceReaderType.FullName,
			};
			foreach (var type in module.Types) {
				if (type.Namespace != "")
					continue;
				if (type.BaseType == null || type.BaseType.EType != ElementType.Object)
					continue;
				if (!hasInterface(type, "System.Collections.IDictionaryEnumerator"))
					continue;
				if (!new FieldTypes(type).all(resourceEnumeratorType_fields))
					continue;
				var ctor = DotNetUtils.getMethod(type, ".ctor");
				if (ctor == null)
					continue;
				if (ctor.Parameters.Count != 1)
					continue;
				if (ctor.Parameters[0].ParameterType != encryptedResourceReaderType)
					continue;

				resourceEnumeratorType = type;
				return;
			}
		}

		void findResourceFlags() {
			if (resTypeCtor == null || resTypeCtor.Parameters.Count != 4)
				return;
			var type = resTypeCtor.Parameters[2].ParameterType as TypeDefinition;
			if (type == null || !type.IsEnum)
				return;

			resourceFlagsType = type;
		}

		static string[] resType_fields = new string[] {
			"System.Int32",
			"System.Object",
			"System.String",
		};
		void findResType() {
			if (encryptedResourceReaderTypeDict == null)
				return;
			var type = encryptedResourceReaderTypeDict.GenericArguments[1] as TypeDefinition;
			if (type == null)
				return;
			if (type.BaseType == null || type.BaseType.EType != ElementType.Object)
				return;
			var ctor = DotNetUtils.getMethod(type, ".ctor");
			if (ctor == null || ctor.Parameters.Count != 4)
				return;

			resTypeCtor = ctor;
			resType = type;
		}

		static string[] encryptedResourceReaderType_fields = new string[] {
			"System.Boolean",
			"System.Int32",
			"System.Int64",
			"System.IO.BinaryReader",
			"System.Runtime.Serialization.Formatters.Binary.BinaryFormatter",
		};
		void findEncryptedResourceReader() {
			var type = getTypeFromCode(encryptedResourceSet_GetDefaultReader);
			if (type == null)
				return;
			if (type.BaseType == null || !hasInterface(type, "System.Resources.IResourceReader"))
				return;
			if (!new FieldTypes(type).all(encryptedResourceReaderType_fields))
				return;
			var dictType = getDlxResDict(type);
			if (dictType == null)
				return;
			if (findXxteaMethod(type) == null)
				return;

			encryptedResourceReaderType = type;
			encryptedResourceReaderTypeDict = dictType;
		}

		static bool hasInterface(TypeDefinition type, string interfaceFullName) {
			foreach (var iface in type.Interfaces) {
				if (iface.FullName == interfaceFullName)
					return true;
			}
			return false;
		}

		static GenericInstanceType getDlxResDict(TypeDefinition type) {
			foreach (var field in type.Fields) {
				var fieldType = field.FieldType as GenericInstanceType;
				if (fieldType == null)
					continue;
				if (fieldType.ElementType.FullName != "System.Collections.Generic.Dictionary`2")
					continue;
				if (fieldType.GenericArguments.Count != 2)
					continue;
				if (fieldType.GenericArguments[0].FullName != "System.String")
					continue;
				if (!(fieldType.GenericArguments[1] is TypeDefinition))
					continue;
				return fieldType;
			}
			return null;
		}

		static TypeDefinition getTypeFromCode(MethodDefinition method) {
			if (method == null || method.Body == null)
				return null;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var type = instr.Operand as TypeDefinition;
				if (type != null)
					return type;
			}

			return null;
		}

		void findEncryptedResourceSet() {
			foreach (var type in module.Types) {
				if (type.Namespace != "")
					continue;
				if (type.BaseType == null || type.BaseType.FullName != "System.Resources.ResourceSet")
					continue;
				var ctor = DotNetUtils.getMethod(type, ".ctor");
				if (!DotNetUtils.isMethod(ctor, "System.Void", "(System.Resources.IResourceReader)"))
					continue;
				var method = DotNetUtils.getMethod(type, "GetDefaultReader");
				if (!DotNetUtils.isMethod(method, "System.Type", "()"))
					continue;
				if (method.Body == null || method.IsStatic || !method.IsVirtual)
					continue;

				encryptedResourceSet_GetDefaultReader = method;
				encryptedResourceSetType = type;
				return;
			}
		}

		static string[] encryptedResourceStreamType_fields = new string[] {
			"System.Byte",
			"System.Byte[]",
			"System.Boolean",
			"System.Int32",
			"System.Int64",
			"System.IO.MemoryStream",
			"System.IO.Stream",
			"System.UInt32[]",
		};
		void findEncryptedResourceStreamType() {
			foreach (var type in module.Types) {
				if (type.Namespace != "")
					continue;
				if (type.BaseType == null || type.BaseType.FullName != "System.IO.Stream")
					continue;
				var ctor = DotNetUtils.getMethod(type, ".ctor");
				if (!DotNetUtils.isMethod(ctor, "System.Void", "(System.IO.Stream)"))
					continue;
				if (!new FieldTypes(type).all(encryptedResourceStreamType_fields))
					continue;
				if (findXxteaMethod(type) == null)
					continue;

				MethodDefinition getManifestResourceStreamMethodTmp1, getManifestResourceStreamMethodTmp2;
				if (!findManifestResourceStreamMethods(type, out getManifestResourceStreamMethodTmp1, out getManifestResourceStreamMethodTmp2))
					continue;

				methodsRestorer.createGetManifestResourceStream1(getManifestResourceStreamMethodTmp1);
				methodsRestorer.createGetManifestResourceStream2(getManifestResourceStreamMethodTmp2);
				encryptedResourceStreamType = type;
				return;
			}
		}

		static MethodDefinition findXxteaMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsPrivate || method.IsStatic || method.Body == null)
					continue;
				if (DotNetUtils.isMethod(method, "System.Void", "(System.UInt32[],System.UInt32[])")) {
					if (!DeobUtils.hasInteger(method, 0x9E3779B9))
						continue;
				}
				else if (DotNetUtils.isMethod(method, "System.Void", "(System.UInt32[],System.UInt32[],System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32,System.UInt32)")) {
					// Here if 5.0. 0x9E3779B9 is passed to it as the last arg.
				}
				else
					continue;
				if (!DeobUtils.hasInteger(method, 52))
					continue;

				return method;
			}
			return null;
		}

		static bool findManifestResourceStreamMethods(TypeDefinition type, out MethodDefinition getManifestResourceStreamMethodTmp1, out MethodDefinition getManifestResourceStreamMethodTmp2) {
			getManifestResourceStreamMethodTmp1 = null;
			getManifestResourceStreamMethodTmp2 = null;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (DotNetUtils.isMethod(method, "System.IO.Stream", "(System.Reflection.Assembly,System.String)"))
					getManifestResourceStreamMethodTmp1 = method;
				else if (DotNetUtils.isMethod(method, "System.IO.Stream", "(System.Reflection.Assembly,System.Type,System.String)"))
					getManifestResourceStreamMethodTmp2 = method;
			}
			return getManifestResourceStreamMethodTmp1 != null && getManifestResourceStreamMethodTmp2 != null;
		}

		public void decrypt() {
			for (int i = 0; i < module.Resources.Count; i++) {
				var resource = module.Resources[i] as EmbeddedResource;
				if (resource == null)
					continue;

				var decrypted = decrypt(resource.GetResourceStream());
				if (decrypted == null)
					continue;

				Log.v("Decrypted resource {0}", Utils.toCsharpString(resource.Name));
				module.Resources[i] = new EmbeddedResource(resource.Name, resource.Attributes, decrypted);
			}
		}

		byte[] decrypt(Stream stream) {
			try {
				stream.Position = 0;
				var reader = new BinaryReader(stream);
				uint sig = reader.ReadUInt32();
				stream.Position = 0;
				if (sig == 0xBEEFCACE)
					return decryptBeefcace(reader);
				if (sig == 0x58455245)
					return decryptErex(reader);
				return null;
			}
			catch (InvalidDataException) {
				return null;
			}
			catch (Exception ex) {
				Log.w("Got an exception when decrypting resources: {0} - {1}", ex.GetType(), ex.Message);
				return null;
			}
		}

		byte[] decryptBeefcace(BinaryReader reader) {
			var resourceReader = new ResourceReader(reader);
			return new ResourceConverter(module, resourceReader.read()).convert();
		}

		byte[] decryptErex(BinaryReader reader) {
			return new ErexResourceReader(reader.BaseStream).decrypt();
		}

		public void deobfuscate(Blocks blocks) {
			if (encryptedResourceStreamType == null)
				return;

			methodsRestorer.deobfuscate(blocks);
		}
	}
}
