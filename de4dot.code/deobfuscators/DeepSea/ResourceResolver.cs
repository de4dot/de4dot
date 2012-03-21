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
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	class ResourceResolver : ResolverBase {
		EmbeddedResource resource;
		FieldDefinition resourceField;
		MethodDefinition getDataMethod;
		int magicV4;
		bool isV3;

		public MethodDefinition GetDataMethod {
			get { return getDataMethod; }
		}

		public EmbeddedResource Resource {
			get { return resource; }
		}

		public ResourceResolver(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob)
			: base(module, simpleDeobfuscator, deob) {
		}

		protected override bool checkResolverInitMethodInternal(MethodDefinition resolverInitMethod) {
			return DotNetUtils.callsMethod(resolverInitMethod, "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)");
		}

		protected override bool checkHandlerMethodDesktopInternal(MethodDefinition handler) {
			if (checkHandlerV3(handler)) {
				isV3 = true;
				return true;
			}

			FieldDefinition resourceFieldTmp;
			MethodDefinition getDataMethodTmp;
			simpleDeobfuscator.deobfuscate(handler);
			if (checkHandlerV4(handler, out resourceFieldTmp, out getDataMethodTmp, out magicV4)) {
				isV3 = false;
				resourceField = resourceFieldTmp;
				getDataMethod = getDataMethodTmp;
				return true;
			}

			return false;
		}

		static string[] handlerLocalTypes_V3 = new string[] {
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
		static bool checkHandlerV3(MethodDefinition handler) {
			return new LocalTypes(handler).all(handlerLocalTypes_V3);
		}

		static bool checkHandlerV4(MethodDefinition handler, out FieldDefinition resourceField, out MethodDefinition getDataMethod, out int magic) {
			var instrs = handler.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = i;

				if (instrs[index++].OpCode.Code != Code.Ldarg_1)
					continue;

				var ldtoken = instrs[index++];
				if (ldtoken.OpCode.Code != Code.Ldtoken)
					continue;
				var field = ldtoken.Operand as FieldDefinition;

				string methodSig = "(System.ResolveEventArgs,System.RuntimeFieldHandle,System.Int32,System.String,System.Int32)";
				var method = ldtoken.Operand as MethodDefinition;
				if (method != null) {
					// >= 4.0.4
					if (!DotNetUtils.isMethod(method, "System.Byte[]", "()"))
						continue;
					field = getResourceField(method);
					methodSig = "(System.ResolveEventArgs,System.RuntimeMethodHandle,System.Int32,System.String,System.Int32)";
				}
				else {
					// 4.0.1.18 .. 4.0.3
				}

				if (field.InitialValue == null || field.InitialValue.Length == 0)
					continue;

				var ldci4_len = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_len))
					continue;
				if (DotNetUtils.getLdcI4Value(ldci4_len) != field.InitialValue.Length)
					continue;

				if (instrs[index++].OpCode.Code != Code.Ldstr)
					continue;

				var ldci4_magic = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_magic))
					continue;
				magic = DotNetUtils.getLdcI4Value(ldci4_magic);

				var call = instrs[index++];
				if (call.OpCode.Code == Code.Tail)
					call = instrs[index++];
				if (call.OpCode.Code != Code.Call)
					continue;
				if (!DotNetUtils.isMethod(call.Operand as MethodReference, "System.Reflection.Assembly", methodSig))
					continue;

				resourceField = field;
				getDataMethod = method;
				return true;
			}

			magic = 0;
			resourceField = null;
			getDataMethod = null;
			return false;
		}

		static FieldDefinition getResourceField(MethodDefinition method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldtoken)
					continue;
				var field = instr.Operand as FieldDefinition;
				if (field == null || field.InitialValue == null || field.InitialValue.Length == 0)
					continue;
				return field;
			}
			return null;
		}

		public void initialize() {
			if (resolveHandler == null)
				return;

			if (isV3) {
				simpleDeobfuscator.deobfuscate(resolveHandler);
				simpleDeobfuscator.decryptStrings(resolveHandler, deob);
				resource = DeobUtils.getEmbeddedResourceFromCodeStrings(module, resolveHandler);
				if (resource == null) {
					Log.w("Could not find resource of encrypted resources");
					return;
				}
			}
		}

		public bool mergeResources(out EmbeddedResource rsrc) {
			rsrc = null;

			if (isV3) {
				if (resource == null)
					return false;

				DeobUtils.decryptAndAddResources(module, resource.Name, () => decryptResourceV3(resource));
				rsrc = resource;
			}
			else {
				if (resourceField == null)
					return false;

				string name = string.Format("Embedded data field {0:X8} RVA {1:X8}", resourceField.MetadataToken.ToInt32(), resourceField.RVA);
				DeobUtils.decryptAndAddResources(module, name, () => decryptResourceV4(resourceField.InitialValue, magicV4));
				resourceField.InitialValue = new byte[1];
				resourceField.FieldType = module.TypeSystem.Byte;
			}
			return true;
		}
	}
}
