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

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Agile_NET {
	class StringDecrypter {
		ModuleDefMD module;
		TypeDef stringDecrypterType;
		FieldDef keyInitField;
		FieldDef keyArrayField;
		Dictionary<StringDecrypterInfo, bool> stringDecrypterInfos = new Dictionary<StringDecrypterInfo, bool>();
		byte[] stringDecrypterKey;

		public bool Detected => stringDecrypterInfos.Count != 0;
		public TypeDef Type => stringDecrypterType;
		public TypeDef KeyArrayFieldType => keyArrayField?.DeclaringType;
		public IEnumerable<StringDecrypterInfo> StringDecrypterInfos => stringDecrypterInfos.Keys;

		public StringDecrypter(ModuleDefMD module, IEnumerable<StringDecrypterInfo> stringDecrypterMethods) {
			this.module = module;
			foreach (var sdm in stringDecrypterMethods)
				stringDecrypterInfos[sdm] = true;
		}

		public StringDecrypter(ModuleDefMD module, StringDecrypter oldOne) {
			this.module = module;
			stringDecrypterType = Lookup(oldOne.stringDecrypterType, "Could not find stringDecrypterType");
			keyInitField = Lookup(oldOne.keyInitField, "Could not find key init field");
			keyArrayField = Lookup(oldOne.keyArrayField, "Could not find key array field");
			foreach (var info in oldOne.stringDecrypterInfos.Keys) {
				var m = Lookup(info.Method, "Could not find string decrypter method");
				var f = Lookup(info.Field, "Could not find string decrypter field");
				stringDecrypterInfos[new StringDecrypterInfo(m, f)] = true;
			}
			stringDecrypterKey = oldOne.stringDecrypterKey;
		}

		T Lookup<T>(T def, string errorMessage) where T : class, ICodedToken => DeobUtils.Lookup(module, def, errorMessage);

		public void AddDecrypterInfos(IEnumerable<StringDecrypterInfo> infos) {
			foreach (var info in infos)
				stringDecrypterInfos[info] = true;
		}

		public void Find() {
			stringDecrypterKey = new byte[1] { 0xFF };
			foreach (var type in module.Types) {
				if (type.FullName == "<D234>" || type.FullName == "<ClassD234>") {
					stringDecrypterType = type;
					foreach (var field in type.Fields) {
						if (field.FullName == "<D234> <D234>::345" || field.FullName == "<ClassD234>/D234 <ClassD234>::345") {
							keyInitField = field;
							stringDecrypterKey = field.InitialValue;
							break;
						}
					}
					break;
				}
			}
		}

		public void Initialize() {
			if (keyInitField == null)
				return;

			foreach (var type in module.Types) {
				var cctor = type.FindStaticConstructor();
				if (cctor == null)
					continue;
				keyArrayField = GetKeyArrayField(cctor, keyInitField);
				if (keyArrayField != null)
					break;
			}
			if (keyArrayField == null)
				return;

			foreach (var type in module.GetTypes()) {
				var method = FindStringDecrypters(type, keyArrayField, out var field);
				if (method == null)
					continue;

				stringDecrypterInfos[new StringDecrypterInfo(method, field)] = true;
			}
		}

		static FieldDef GetKeyArrayField(MethodDef method, FieldDef keyInitField) {
			if (method == null || method.Body == null)
				return null;
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				if (instrs[i].OpCode.Code != Code.Ldtoken)
					continue;

				i++;
				for (; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code != Code.Stsfld)
						continue;
					var field = instr.Operand as FieldDef;
					if (field == null || !field.IsStatic || field.DeclaringType != method.DeclaringType)
						continue;
					if (field.FieldSig.GetFieldType().GetFullName() != "System.Byte[]")
						continue;
					return field;
				}
			}
			return null;
		}

		static MethodDef FindStringDecrypters(TypeDef type, FieldDef keyArrayField, out FieldDef field) {
			FieldDef foundField = null;
			foreach (var method in type.Methods) {
				if (!method.IsAssembly || !method.IsStatic)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.String", "(System.String)"))
					continue;
				if (!method.HasBody)
					continue;

				bool accessedArrayField = false;
				foreach (var instr in method.Body.Instructions) {
					var f = instr.Operand as FieldDef;
					accessedArrayField |= f == keyArrayField;
					if (f == null || f == keyArrayField || f == foundField)
						continue;
					if (DotNetUtils.DerivesFromDelegate(f.DeclaringType))
						continue;
					if (f.FieldSig.GetFieldType().GetFullName() != "System.Collections.Hashtable" ||
						foundField != null)
						goto exit;
					foundField = f;
				}
				if (!accessedArrayField)
					continue;

				field = foundField;
				return method;
			}

exit: ;
			field = null;
			return null;
		}

		public void Deobfuscate(Blocks blocks) {
			if (!blocks.Method.IsStaticConstructor)
				return;

			var decrypterFields = new Dictionary<FieldDef, bool>(stringDecrypterInfos.Count);
			foreach (var info in stringDecrypterInfos.Keys) {
				if (info.Field != null)
					decrypterFields[info.Field] = true;
			}

			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = instrs.Count - 2; i >= 0; i--) {
					var newobj = instrs[i];
					if (newobj.OpCode.Code != Code.Newobj)
						continue;
					var ctor = newobj.Operand as IMethod;
					if (ctor == null || ctor.FullName != "System.Void System.Collections.Hashtable::.ctor()")
						continue;
					var stsfld = instrs[i + 1];
					if (stsfld.OpCode.Code != Code.Stsfld)
						continue;
					var field = stsfld.Operand as FieldDef;
					if (field == null || !decrypterFields.ContainsKey(field))
						continue;

					block.Remove(i, 2);
				}
			}
		}

		public string Decrypt(string es) {
			if (stringDecrypterKey == null)
				throw new ApplicationException("Trying to decrypt strings when stringDecrypterKey is null (could not find it!)");
			char[] buf = new char[es.Length];
			for (int i = 0; i < es.Length; i++)
				buf[i] = (char)(es[i] ^ stringDecrypterKey[i % stringDecrypterKey.Length]);
			return new string(buf);
		}
	}
}
