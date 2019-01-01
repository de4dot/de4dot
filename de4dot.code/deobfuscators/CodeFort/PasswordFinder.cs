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
using System.Reflection;
using System.Reflection.Emit;

namespace de4dot.code.deobfuscators.CodeFort {
	class PasswordInfo {
		public string passphrase;
		public string salt;
		public string iv;

		public PasswordInfo(string passphrase, string salt, string iv) {
			this.passphrase = passphrase;
			this.salt = salt;
			this.iv = iv;
		}

		public override string ToString() => $"P:{passphrase}, S:{salt}, I:{iv}";
	}

	class PasswordFinder {
		byte[] serializedData;
		System.Collections.IList asmTypes;

		class Obj {
			object obj;

			public Obj(object obj) => this.obj = obj;
			public string Name => (string)ReadField("Name");
			public List<Obj> Members => GetList("Members");
			public List<Obj> Instructions => GetList("Instructions");
			public object Operand => ReadField("Operand");
			public string OpCode => (string)ReadField("OpCode");
			public Obj MemberDef => new Obj(ReadField("MemberDef"));
			protected object ReadField(string name) => PasswordFinder.ReadField(obj, name);

			public Obj FindMethod(string name) {
				foreach (var member in Members) {
					if (member.obj.GetType().ToString() != "MethodDef")
						continue;
					if (member.Name != name)
						continue;

					return member;
				}

				throw new ApplicationException($"Could not find method {name}");
			}

			List<Obj> GetList(string name) => ConvertList((System.Collections.IList)ReadField(name));

			static List<Obj> ConvertList(System.Collections.IList inList) {
				var outList = new List<Obj>(inList.Count);
				foreach (var e in inList)
					outList.Add(new Obj(e));
				return outList;
			}

			public override string ToString() => Name;
		}

		public PasswordFinder(byte[] serializedData) => this.serializedData = serializedData;
		static object ReadField(object instance, string name) => instance.GetType().GetField(name).GetValue(instance);
		static System.Collections.IList ToList(object obj) => (System.Collections.IList)obj;

		public void Find(out PasswordInfo mainAsmPassword, out PasswordInfo embedPassword) {
			var asmName = new AssemblyName("asm");
			const AssemblyBuilderAccess BuilderAccess = AssemblyBuilderAccess.Run;
#if NETFRAMEWORK
			var asmBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(asmName, BuilderAccess);
#else
			var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, BuilderAccess);
#endif
			var moduleBuilder = asmBuilder.DefineDynamicModule("mod");
			var serializedTypes = new SerializedTypes(moduleBuilder);
			var allTypes = serializedTypes.Deserialize(serializedData);
			asmTypes = ToList(ReadField(allTypes, "Types"));

			mainAsmPassword = FindMainAssemblyPassword();
			embedPassword = FindEmbedPassword();
		}

		Obj FindType(string name) {
			foreach (var tmp in asmTypes) {
				var type = new Obj(tmp);
				if (type.Name == name)
					return type;
			}
			return null;
		}

		PasswordInfo FindMainAssemblyPassword() {
			var type = FindType("BootstrapDynArguments");
			var cctor = type.FindMethod(".cctor");
			var instrs = cctor.Instructions;
			var passphrase = FindStringStoreValue(instrs, "KeyPassphrase");
			var salt = FindStringStoreValue(instrs, "KeySaltValue");
			var iv = FindStringStoreValue(instrs, "KeyIV");
			return new PasswordInfo(passphrase, salt, iv);
		}

		static string FindStringStoreValue(List<Obj> instrs, string fieldName) {
			for (int i = 0; i < instrs.Count - 1; i++) {
				var ldstr = instrs[i];
				if (ldstr.OpCode != "ldstr")
					continue;
				var stsfld = instrs[i + 1];
				if (stsfld.OpCode != "stsfld")
					continue;
				var memberRef = new Obj(stsfld.Operand);
				if (memberRef.MemberDef == null)
					continue;
				if (memberRef.MemberDef.Name != fieldName)
					continue;

				return (string)ldstr.Operand;
			}

			return null;
		}

		PasswordInfo FindEmbedPassword() {
			var type = FindType("CilEmbeddingHelper");
			if (type == null)
				return null;
			var method = type.FindMethod("CurrentDomain_AssemblyResolve");
			var instrs = method.Instructions;
			for (int i = 0; i < instrs.Count - 3; i++) {
				int index = i;

				var ldstr1 = instrs[index++];
				if (ldstr1.OpCode != "ldstr")
					continue;
				var passphrase = GetString(ldstr1, instrs, ref index);

				var ldstr2 = instrs[index++];
				if (ldstr2.OpCode != "ldstr")
					continue;
				var salt = GetString(ldstr2, instrs, ref index);

				var ldc = instrs[index++];
				if (!ldc.OpCode.StartsWith("ldc.i4"))
					continue;

				var ldstr3 = instrs[index++];
				if (ldstr3.OpCode != "ldstr")
					continue;
				var iv = GetString(ldstr3, instrs, ref index);

				return new PasswordInfo(passphrase, salt, iv);
			}

			return null;
		}

		static string GetString(Obj ldstr, List<Obj> instrs, ref int index) {
			var s = (string)ldstr.Operand;
			if (index >= instrs.Count)
				return s;
			var call = instrs[index];
			if (call.OpCode != "call" && call.OpCode != "callvirt")
				return s;
			index++;
			var op = new Obj(call.Operand);
			if (op.Name == "ToUpper")
				return s.ToUpper();
			if (op.Name == "ToLower")
				return s.ToLower();
			throw new ApplicationException($"Unknown method {op.Name}");
		}
	}
}
