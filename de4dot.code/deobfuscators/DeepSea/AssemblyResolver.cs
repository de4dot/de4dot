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
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	class AssemblyResolver : ResolverBase {
		Version version;
		List<FieldInfo> fieldInfos;

		enum Version {
			Unknown,
			V3Old,
			V3,
			V4,
		}

		public class AssemblyInfo {
			public byte[] data;
			public string fullName;
			public string simpleName;
			public string extension;
			public EmbeddedResource resource;

			public AssemblyInfo(byte[] data, string fullName, string simpleName, string extension, EmbeddedResource resource) {
				this.data = data;
				this.fullName = fullName;
				this.simpleName = simpleName;
				this.extension = extension;
				this.resource = resource;
			}

			public override string ToString() {
				return fullName;
			}
		}

		class FieldInfo {
			public FieldDefinition field;
			public int magic;

			public FieldInfo(FieldDefinition field, int magic) {
				this.field = field;
				this.magic = magic;
			}
		}

		public AssemblyResolver(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob)
			: base(module, simpleDeobfuscator, deob) {
		}

		protected override bool checkResolverInitMethodInternal(MethodDefinition resolverInitMethod) {
			return checkIfCalled(resolverInitMethod, "System.Void System.AppDomain::add_AssemblyResolve(System.ResolveEventHandler)");
		}

		protected override bool checkHandlerMethodInternal(MethodDefinition handler) {
			if (checkHandlerV3(handler) || checkHandlerSL(handler)) {
				if (isV3Old(handler))
					version = Version.V3Old;
				else
					version = Version.V3;
				return true;
			}

			simpleDeobfuscator.deobfuscate(handler);
			List<FieldInfo> fieldInfosTmp;
			if (checkHandlerV4(handler, out fieldInfosTmp) ||
				checkHandlerV4_0_4(handler, out fieldInfosTmp)) {
				version = Version.V4;
				fieldInfos = fieldInfosTmp;
				return true;
			}

			return false;
		}

		static bool isV3Old(MethodDefinition method) {
			return DotNetUtils.callsMethod(method, "System.Int32 System.IO.Stream::Read(System.Byte[],System.Int32,System.Int32)") &&
				!DotNetUtils.callsMethod(method, "System.Int32 System.IO.Stream::ReadByte()");
		}

		static string[] handlerLocalTypes_NET = new string[] {
			"System.Byte[]",
			"System.IO.Compression.DeflateStream",
			"System.IO.MemoryStream",
			"System.IO.Stream",
			"System.Reflection.Assembly",
			"System.Security.Cryptography.SHA1CryptoServiceProvider",
			"System.String",
		};
		static bool checkHandlerV3(MethodDefinition handler) {
			return new LocalTypes(handler).all(handlerLocalTypes_NET);
		}

		static string[] handlerLocalTypes_SL = new string[] {
			"System.Byte[]",
			"System.IO.Stream",
			"System.Reflection.Assembly",
			"System.Security.Cryptography.SHA1Managed",
			"System.String",
			"System.Windows.AssemblyPart",
		};
		static bool checkHandlerSL(MethodDefinition handler) {
			return new LocalTypes(handler).all(handlerLocalTypes_SL);
		}

		// 4.0.1.18 .. 4.0.3
		bool checkHandlerV4(MethodDefinition handler, out List<FieldInfo> fieldInfos) {
			fieldInfos = new List<FieldInfo>();

			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				int index = i;

				var ldtoken = instrs[index++];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDefinition;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					return false;

				var ldci4_len = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_len))
					return false;
				if (DotNetUtils.getLdcI4Value(ldci4_len) != field.InitialValue.Length)
					return false;

				var ldci4_magic = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_magic))
					return false;
				int magic = DotNetUtils.getLdcI4Value(ldci4_magic);

				var call = instrs[index++];
				if (call.OpCode.Code == Code.Tail)
					call = instrs[index++];
				if (call.OpCode.Code != Code.Call)
					return false;
				if (!DotNetUtils.isMethod(call.Operand as MethodReference, "System.Reflection.Assembly", "(System.RuntimeFieldHandle,System.Int32,System.Int32)"))
					return false;

				fieldInfos.Add(new FieldInfo(field, magic));
			}

			return fieldInfos.Count != 0;
		}

		// 4.0.4+
		bool checkHandlerV4_0_4(MethodDefinition handler, out List<FieldInfo> fieldInfos) {
			fieldInfos = new List<FieldInfo>();

			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count - 8; i++) {
				int index = i;

				var ldci4_len = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_len))
					continue;
				if (instrs[index++].OpCode.Code != Code.Newarr)
					continue;
				if (!DotNetUtils.isStloc(instrs[index++]))
					continue;
				if (!DotNetUtils.isLdloc(instrs[index++]))
					continue;

				var ldtoken = instrs[index++];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDefinition;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;

				var call1 = instrs[index++];
				if (call1.OpCode.Code != Code.Call)
					continue;
				if (!DotNetUtils.isMethod(call1.Operand as MethodReference, "System.Void", "(System.Array,System.RuntimeFieldHandle)"))
					continue;

				if (!DotNetUtils.isLdloc(instrs[index++]))
					continue;

				var ldci4_magic = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_magic))
					continue;
				int magic = DotNetUtils.getLdcI4Value(ldci4_magic);

				var call2 = instrs[index++];
				if (call2.OpCode.Code == Code.Tail)
					call2 = instrs[index++];
				if (call2.OpCode.Code != Code.Call)
					continue;
				if (!DotNetUtils.isMethod(call2.Operand as MethodReference, "System.Reflection.Assembly", "(System.Byte[],System.Int32)"))
					continue;

				fieldInfos.Add(new FieldInfo(field, magic));
			}

			return fieldInfos.Count != 0;
		}

		public IEnumerable<AssemblyInfo> getAssemblyInfos() {
			if (!Detected)
				return new List<AssemblyInfo>();

			switch (version) {
			case Version.V3Old:
			case Version.V3:
				return getAssemblyInfosV3();
			case Version.V4:
				return getAssemblyInfosV4();
			default:
				throw new ApplicationException("Unknown version");
			}
		}

		IEnumerable<AssemblyInfo> getAssemblyInfosV3() {
			var infos = new List<AssemblyInfo>();

			foreach (var tmp in module.Resources) {
				var resource = tmp as EmbeddedResource;
				if (resource == null)
					continue;
				if (!Regex.IsMatch(resource.Name, @"^[0-9A-F]{40}$"))
					continue;
				var info = getAssemblyInfoV3(resource);
				if (info == null)
					continue;
				infos.Add(info);
			}

			return infos;
		}

		AssemblyInfo getAssemblyInfoV3(EmbeddedResource resource) {
			try {
				var decrypted = version == Version.V3Old ? decryptResourceV3Old(resource) : decryptResourceV3(resource);
				return getAssemblyInfo(decrypted, resource);
			}
			catch (Exception) {
				return null;
			}
		}

		AssemblyInfo getAssemblyInfo(byte[] decryptedData, EmbeddedResource resource) {
			var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(decryptedData));
			var fullName = asm.Name.FullName;
			var simpleName = asm.Name.Name;
			var extension = DeobUtils.getExtension(asm.Modules[0].Kind);
			return new AssemblyInfo(decryptedData, fullName, simpleName, extension, resource);
		}

		IEnumerable<AssemblyInfo> getAssemblyInfosV4() {
			var infos = new List<AssemblyInfo>();

			if (fieldInfos == null)
				return infos;

			foreach (var fieldInfo in fieldInfos) {
				var decrypted = decryptResourceV4(fieldInfo.field.InitialValue, fieldInfo.magic);
				infos.Add(getAssemblyInfo(decrypted, null));
				fieldInfo.field.InitialValue = new byte[1];
				fieldInfo.field.FieldType = module.TypeSystem.Byte;
			}

			return infos;
		}
	}
}
