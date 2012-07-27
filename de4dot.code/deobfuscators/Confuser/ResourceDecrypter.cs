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

using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class ResourceDecrypter {
		ModuleDefinition module;
		ISimpleDeobfuscator simpleDeobfuscator;
		MethodDefinition handler;
		MethodDefinition installMethod;
		EmbeddedResource resource;
		Dictionary<FieldDefinition, bool> fields = new Dictionary<FieldDefinition, bool>();
		byte key0, key1;

		public IEnumerable<FieldDefinition> Fields {
			get { return fields.Keys; }
		}

		public MethodDefinition Handler {
			get { return handler; }
		}

		public bool Detected {
			get { return handler != null; }
		}

		public ResourceDecrypter(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
		}

		public void find() {
			if (checkMethod(DotNetUtils.getModuleTypeCctor(module)))
				return;
		}

		bool checkMethod(MethodDefinition method) {
			if (method == null || method.Body == null)
				return false;
			if (!DotNetUtils.callsMethod(method, "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)"))
				return false;
			simpleDeobfuscator.deobfuscate(method, true);
			fields.Clear();

			var tmpHandler = getHandler(method);
			if (tmpHandler == null || tmpHandler.DeclaringType != method.DeclaringType)
				return false;

			simpleDeobfuscator.deobfuscate(tmpHandler, true);
			if (addFields(findFields(tmpHandler, method.DeclaringType)) != 1)
				return false;

			var tmpResource = findResource(tmpHandler);
			if (tmpResource == null)
				return false;

			if (!findKey0(tmpHandler, out key0))
				return false;
			if (!findKey1(tmpHandler, out key1))
				return false;

			handler = tmpHandler;
			resource = tmpResource;
			installMethod = method;
			return true;
		}

		static MethodDefinition getHandler(MethodDefinition method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 2; i++) {
				var ldftn = instrs[i];
				if (ldftn.OpCode.Code != Code.Ldftn)
					continue;
				var handler = ldftn.Operand as MethodDefinition;
				if (handler == null)
					continue;

				var newobj = instrs[i + 1];
				if (newobj.OpCode.Code != Code.Newobj)
					continue;

				var callvirt = instrs[i + 2];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;
				var calledMethod = callvirt.Operand as MethodReference;
				if (calledMethod == null)
					continue;
				if (calledMethod.FullName != "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)")
					continue;

				return handler;
			}
			return null;
		}

		int addFields(IEnumerable<FieldDefinition> moreFields) {
			int count = 0;
			foreach (var field in moreFields) {
				if (addField(field))
					count++;
			}
			return count;
		}

		bool addField(FieldDefinition field) {
			if (field == null)
				return false;
			if (fields.ContainsKey(field))
				return false;
			fields[field] = true;
			return true;
		}

		static IEnumerable<FieldDefinition> findFields(MethodDefinition method, TypeDefinition declaringType) {
			var fields = new List<FieldDefinition>();
			foreach (var instr in method.Body.Instructions) {
				var field = instr.Operand as FieldDefinition;
				if (field != null && field.DeclaringType == declaringType)
					fields.Add(field);
			}
			return fields;
		}

		EmbeddedResource findResource(MethodDefinition method) {
			return DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
		}

		static bool findKey0(MethodDefinition method, out byte key0) {
			var instrs = method.Body.Instructions;
			for (int index = 0; index < instrs.Count; index++) {
				index = ConfuserUtils.findCallMethod(instrs, index, Code.Callvirt, "System.Int32 System.IO.Stream::Read(System.Byte[],System.Int32,System.Int32)");
				if (index < 0)
					break;

				if (index + 4 >= instrs.Count)
					break;
				index++;

				if (instrs[index++].OpCode.Code != Code.Pop)
					continue;
				var ldci4 = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[index++].OpCode.Code != Code.Conv_U1)
					continue;
				if (!DotNetUtils.isStloc(instrs[index++]))
					continue;

				key0 = (byte)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}

			key0 = 0;
			return false;
		}

		static bool findKey1(MethodDefinition method, out byte key1) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				int index = i;
				if (!DotNetUtils.isLdloc(instrs[index++]))
					continue;
				var ldci4_1 = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_1))
					continue;
				if (instrs[index++].OpCode.Code != Code.Mul)
					continue;
				var ldci4_2 = instrs[index++];
				if (!DotNetUtils.isLdcI4(ldci4_2) || DotNetUtils.getLdcI4Value(ldci4_2) != 0x100)
					continue;
				if (instrs[index++].OpCode.Code != Code.Rem)
					continue;

				key1 = (byte)DotNetUtils.getLdcI4Value(ldci4_1);
				return true;
			}

			key1 = 0;
			return false;
		}

		public EmbeddedResource mergeResources() {
			if (resource == null)
				return null;
			DeobUtils.decryptAndAddResources(module, resource.Name, () => decryptResource());
			return resource;
		}

		byte[] decryptResource() {
			var encrypted = resource.GetResourceData();
			byte k = key0;
			for (int i = 0; i < encrypted.Length; i++) {
				encrypted[i] ^= k;
				k *= key1;
			}
			var reader = new BinaryReader(new MemoryStream(DeobUtils.inflate(encrypted, true)));
			return reader.ReadBytes(reader.ReadInt32());
		}

		public void deobfuscate(Blocks blocks) {
			if (blocks.Method != installMethod)
				return;

			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 4; i++) {
					var call = instrs[i];
					if (call.OpCode.Code != Code.Call)
						continue;
					var calledMethod = call.Operand as MethodReference;
					if (calledMethod == null || calledMethod.FullName != "System.AppDomain System.AppDomain::get_CurrentDomain()")
						continue;

					if (instrs[i + 1].OpCode.Code != Code.Ldnull)
						continue;

					var ldftn = instrs[i + 2];
					if (ldftn.OpCode.Code != Code.Ldftn)
						continue;
					if (ldftn.Operand != handler)
						continue;

					var newobj = instrs[i + 3];
					if (newobj.OpCode.Code != Code.Newobj)
						continue;
					var ctor = newobj.Operand as MethodReference;
					if (ctor == null || ctor.FullName != "System.Void System.ResolveEventHandler::.ctor(System.Object,System.IntPtr)")
						continue;

					var callvirt = instrs[i + 4];
					if (callvirt.OpCode.Code != Code.Callvirt)
						continue;
					calledMethod = callvirt.Operand as MethodReference;
					if (calledMethod == null || calledMethod.FullName != "System.Void System.AppDomain::add_ResourceResolve(System.ResolveEventHandler)")
						continue;

					block.remove(i, 5);
					return;
				}
			}
		}
	}
}
