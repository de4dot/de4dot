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
using System.IO;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Babel_NET {
	class StringDecrypter {
		ModuleDefMD module;
		ResourceDecrypter resourceDecrypter;
		ISimpleDeobfuscator simpleDeobfuscator;
		TypeDef decrypterType;
		EmbeddedResource encryptedResource;
		IDecrypterInfo decrypterInfo;

		interface IDecrypterInfo {
			MethodDef Decrypter { get; }
			bool NeedsResource { get; }
			void Initialize(ModuleDefMD module, EmbeddedResource resource);
			string Decrypt(object[] args);
		}

		// Babel .NET 2.x
		class DecrypterInfoV1 : IDecrypterInfo {
			public MethodDef Decrypter { get; set; }
			public bool NeedsResource => false;
			public void Initialize(ModuleDefMD module, EmbeddedResource resource) { }
			public string Decrypt(object[] args) => Decrypt((string)args[0], (int)args[1]);

			string Decrypt(string s, int k) {
				var sb = new StringBuilder(s.Length);
				foreach (var c in s)
					sb.Append((char)(c ^ k));
				return sb.ToString();
			}
		}

		class DecrypterInfoV2 : IDecrypterInfo {
			byte[] key;

			public MethodDef Decrypter { get; set; }
			public bool NeedsResource => true;

			public void Initialize(ModuleDefMD module, EmbeddedResource resource) {
				key = resource.CreateReader().ToArray();
				if (key.Length != 0x100)
					throw new ApplicationException($"Unknown key length: {key.Length}");
			}

			public string Decrypt(object[] args) => Decrypt((string)args[0], (int)args[1]);

			string Decrypt(string s, int k) {
				var sb = new StringBuilder(s.Length);
				byte b = key[(byte)k];
				foreach (var c in s)
					sb.Append((char)(c ^ (b | k)));
				return sb.ToString();
			}
		}

		class DecrypterInfoV3 : IDecrypterInfo {
			Dictionary<int, string> offsetToString = new Dictionary<int, string>();
			ResourceDecrypter resourceDecrypter;
			InstructionEmulator emulator = new InstructionEmulator();

			public IList<Instruction> OffsetCalcInstructions { get; set; }
			public MethodDef Decrypter { get; set; }
			public bool NeedsResource => true;

			public DecrypterInfoV3(ResourceDecrypter resourceDecrypter) => this.resourceDecrypter = resourceDecrypter;

			public void Initialize(ModuleDefMD module, EmbeddedResource resource) {
				var decrypted = resourceDecrypter.Decrypt(resource.CreateReader().ToArray());
				var reader = new BinaryReader(new MemoryStream(decrypted));
				while (reader.BaseStream.Position < reader.BaseStream.Length)
					offsetToString[GetOffset((int)reader.BaseStream.Position)] = reader.ReadString();
			}

			MethodDef dummyMethod;
			int GetOffset(int offset) {
				if (OffsetCalcInstructions == null || OffsetCalcInstructions.Count == 0)
					return offset;
				if (dummyMethod == null) {
					dummyMethod = new MethodDefUser();
					dummyMethod.Body = new CilBody();
				}
				emulator.Initialize(dummyMethod);
				emulator.Push(new Int32Value(offset));
				foreach (var instr in OffsetCalcInstructions)
					emulator.Emulate(instr);
				return ((Int32Value)emulator.Pop()).Value;
			}

			public string Decrypt(object[] args) => Decrypt((int)args[0]);
			string Decrypt(int offset) => offsetToString[offset];
		}

		public bool Detected => decrypterType != null;
		public TypeDef Type => decrypterType;
		public MethodDef DecryptMethod => decrypterInfo?.Decrypter;
		public EmbeddedResource Resource => encryptedResource;

		public StringDecrypter(ModuleDefMD module, ResourceDecrypter resourceDecrypter) {
			this.module = module;
			this.resourceDecrypter = resourceDecrypter;
		}

		public void Find(ISimpleDeobfuscator simpleDeobfuscator) {
			this.simpleDeobfuscator = simpleDeobfuscator;
			foreach (var type in module.Types) {
				var info = CheckDecrypterType(type);
				if (info == null)
					continue;

				decrypterType = type;
				decrypterInfo = info;
				return;
			}
		}

		IDecrypterInfo CheckDecrypterType(TypeDef type) {
			if (type.HasEvents)
				return null;
			if (type.NestedTypes.Count > 2)
				return null;
			if (type.Fields.Count > 1)
				return null;

			foreach (var nested in type.NestedTypes) {
				var info = CheckNested(type, nested);
				if (info != null)
					return info;
			}

			return CheckDecrypterTypeBabel2x(type);
		}

		IDecrypterInfo CheckDecrypterTypeBabel2x(TypeDef type) {
			if (type.HasEvents || type.HasProperties || type.HasNestedTypes)
				return null;
			if (type.HasFields || type.Methods.Count != 1)
				return null;
			var decrypter = type.Methods[0];
			if (!CheckDecryptMethodBabel2x(decrypter))
				return null;

			return new DecrypterInfoV1 { Decrypter = decrypter };
		}

		bool CheckDecryptMethodBabel2x(MethodDef method) {
			if (!method.IsStatic || !method.IsPublic)
				return false;
			if (method.Body == null)
				return false;
			if (method.Name == ".cctor")
				return false;
			if (!DotNetUtils.IsMethod(method, "System.String", "(System.String,System.Int32)"))
				return false;

			int stringLength = 0, stringToCharArray = 0, stringCtor = 0;
			foreach (var instr in method.Body.Instructions) {
				var calledMethod = instr.Operand as IMethod;
				if (calledMethod == null)
					continue;

				switch (instr.OpCode.Code) {
				case Code.Call:
				case Code.Callvirt:
					if (calledMethod.FullName == "System.Int32 System.String::get_Length()")
						stringLength++;
					else if (calledMethod.FullName == "System.Char[] System.String::ToCharArray()")
						stringToCharArray++;
					else
						return false;
					break;

				case Code.Newobj:
					if (calledMethod.FullName == "System.Void System.String::.ctor(System.Char[])")
						stringCtor++;
					else
						return false;
					break;

				default:
					continue;
				}
			}

			return stringLength == 1 && stringToCharArray == 1 && stringCtor == 1;
		}

		IDecrypterInfo CheckNested(TypeDef type, TypeDef nested) {
			if (nested.HasProperties || nested.HasEvents)
				return null;

			if (nested.FindMethod(".ctor") == null)
				return null;

			if (nested.Fields.Count == 1 || nested.Fields.Count == 3) {
				// 4.0+

				if (!HasFieldType(nested.Fields, nested))
					return null;

				var decrypterBuilderMethod = DotNetUtils.GetMethod(nested, "System.Reflection.Emit.MethodBuilder", "(System.Reflection.Emit.TypeBuilder)");
				if (decrypterBuilderMethod == null)
					return null;

				resourceDecrypter.DecryptMethod = ResourceDecrypter.FindDecrypterMethod(nested.FindMethod(".ctor"));

				var nestedDecrypter = DotNetUtils.GetMethod(nested, "System.String", "(System.Int32)");
				if (nestedDecrypter == null || nestedDecrypter.IsStatic)
					return null;
				var decrypter = DotNetUtils.GetMethod(type, "System.String", "(System.Int32)");
				if (decrypter == null || !decrypter.IsStatic)
					return null;

				simpleDeobfuscator.Deobfuscate(decrypterBuilderMethod);
				return new DecrypterInfoV3(resourceDecrypter) {
					Decrypter = decrypter,
					OffsetCalcInstructions = GetOffsetCalcInstructions(decrypterBuilderMethod),
				};
			}
			else if (nested.Fields.Count == 2) {
				// 3.0 - 3.5

				if (CheckFields(nested, "System.Collections.Hashtable", nested)) {
					// 3.0 - 3.5
					var nestedDecrypter = DotNetUtils.GetMethod(nested, "System.String", "(System.Int32)");
					if (nestedDecrypter == null || nestedDecrypter.IsStatic)
						return null;
					var decrypter = DotNetUtils.GetMethod(type, "System.String", "(System.Int32)");
					if (decrypter == null || !decrypter.IsStatic)
						return null;

					resourceDecrypter.DecryptMethod = ResourceDecrypter.FindDecrypterMethod(nested.FindMethod(".ctor"));

					return new DecrypterInfoV3(resourceDecrypter) { Decrypter = decrypter };
				}
				else if (CheckFields(nested, "System.Byte[]", nested)) {
					// 3.0
					var nestedDecrypter = DotNetUtils.GetMethod(nested, "System.String", "(System.String,System.Int32)");
					if (nestedDecrypter == null || nestedDecrypter.IsStatic)
						return null;
					var decrypter = DotNetUtils.GetMethod(type, "System.String", "(System.String,System.Int32)");
					if (decrypter == null || !decrypter.IsStatic)
						return null;

					return new DecrypterInfoV2 { Decrypter = decrypter };
				}
				else
					return null;
			}

			return null;
		}

		class ReflectionToDNLibMethodCreator {
			MethodDef method;
			List<Instruction> instructions = new List<Instruction>();
			InstructionEmulator emulator;
			int index;

			class UserValue : UnknownValue {
				public readonly object obj;
				public UserValue(object obj) => this.obj = obj;
				public override string ToString() {
					if (obj == null)
						return "<null>";
					return obj.ToString();
				}
			}

			public List<Instruction> Instructions => instructions;

			public ReflectionToDNLibMethodCreator(MethodDef method) {
				this.method = method;
				emulator = new InstructionEmulator(method);
			}

			public bool Create() {
				int arrayIndex;
				Value array;
				object value;
				while (true) {
					var instr = method.Body.Instructions[index];
					switch (instr.OpCode.Code) {
					case Code.Ret:
						return true;

					case Code.Newarr:
						var arrayType = (ITypeDefOrRef)instr.Operand;
						int arrayCount = ((Int32Value)emulator.Pop()).Value;
						if (arrayType.FullName == "System.Char")
							emulator.Push(new UserValue(new char[arrayCount]));
						else
							emulator.Push(new UnknownValue());
						break;

					case Code.Call:
					case Code.Callvirt:
						if (!DoCall(instr))
							return false;
						break;

					case Code.Ldelem_U1:
						arrayIndex = ((Int32Value)emulator.Pop()).Value;
						array = (Value)emulator.Pop();
						if (array is UserValue)
							emulator.Push(new Int32Value(((byte[])((UserValue)array).obj)[arrayIndex]));
						else
							emulator.Push(Int32Value.CreateUnknownUInt8());
						break;

					case Code.Stelem_I1:
						value = emulator.Pop();
						arrayIndex = ((Int32Value)emulator.Pop()).Value;
						array = (Value)emulator.Pop();
						if (array is UserValue)
							((byte[])((UserValue)array).obj)[arrayIndex] = (byte)((Int32Value)value).Value;
						break;

					case Code.Stelem_I2:
						value = emulator.Pop();
						arrayIndex = ((Int32Value)emulator.Pop()).Value;
						array = (Value)emulator.Pop();
						if (array is UserValue)
							((char[])((UserValue)array).obj)[arrayIndex] = (char)((Int32Value)value).Value;
						break;

					case Code.Ldelem_Ref:
						arrayIndex = ((Int32Value)emulator.Pop()).Value;
						array = (Value)emulator.Pop();
						var userValue = array as UserValue;
						if (userValue != null && userValue.obj is string[])
							emulator.Push(new StringValue(((string[])userValue.obj)[arrayIndex]));
						else
							emulator.Push(new UnknownValue());
						break;

					case Code.Ldsfld:
						emulator.Push(new UserValue((IField)instr.Operand));
						break;

					default:
						emulator.Emulate(instr);
						break;
					}

					index++;
				}
			}

			bool DoCall(Instruction instr) {
				var calledMethod = (IMethod)instr.Operand;
				var sig = calledMethod.MethodSig;
				var fn = calledMethod.FullName;
				if (fn == "System.Byte[] System.Convert::FromBase64String(System.String)") {
					emulator.Push(new UserValue(Convert.FromBase64String(((StringValue)emulator.Pop()).value)));
					return true;
				}
				else if (fn == "System.String System.Text.Encoding::GetString(System.Byte[])") {
					emulator.Push(new StringValue(Encoding.UTF8.GetString((byte[])((UserValue)emulator.Pop()).obj)));
					return true;
				}
				else if (fn == "System.Int32 System.Int32::Parse(System.String)") {
					emulator.Push(new Int32Value(int.Parse(((StringValue)emulator.Pop()).value)));
					return true;
				}
				else if (fn == "System.String[] System.String::Split(System.Char[])") {
					var ary = (char[])((UserValue)emulator.Pop()).obj;
					var s = ((StringValue)emulator.Pop()).value;
					emulator.Push(new UserValue(s.Split(ary)));
					return true;
				}
				else if (sig != null && sig.HasThis && calledMethod.DeclaringType.FullName == "System.Reflection.Emit.ILGenerator" && calledMethod.Name == "Emit") {
					Value operand = null;
					if (calledMethod.MethodSig.GetParamCount() == 2)
						operand = emulator.Pop();
					var opcode = ReflectionToOpCode((IField)((UserValue)emulator.Pop()).obj);
					emulator.Pop();	// the this ptr
					AddInstruction(new Instruction {
						OpCode = opcode,
						Operand = CreateDNLibOperand(opcode, operand),
					});
					return true;
				}
				else {
					emulator.Emulate(instr);
					return true;
				}
			}

			object CreateDNLibOperand(OpCode opcode, Value op) {
				if (op is Int32Value)
					return ((Int32Value)op).Value;
				if (op is StringValue)
					return ((StringValue)op).value;
				return null;
			}

			void AddInstruction(Instruction instr) => instructions.Add(instr);

			static OpCode ReflectionToOpCode(IField reflectionField) {
				var field = typeof(OpCodes).GetField(reflectionField.Name.String);
				if (field == null || field.FieldType != typeof(OpCode))
					return null;
				return (OpCode)field.GetValue(null);
			}
		}

		static List<Instruction> GetOffsetCalcInstructions(MethodDef method) {
			var creator = new ReflectionToDNLibMethodCreator(method);
			creator.Create();
			var instrs = creator.Instructions;

			int index = 0;

			index = FindInstruction(instrs, index, OpCodes.Conv_I4);
			if (index < 0)
				return null;
			int startInstr = ++index;

			index = FindInstruction(instrs, index, OpCodes.Box);
			if (index < 0)
				return null;
			int endInstr = index - 1;

			var transformInstructions = new List<Instruction>();
			for (int i = startInstr; i <= endInstr; i++)
				transformInstructions.Add(instrs[i]);
			return transformInstructions;
		}

		static int FindInstruction(IList<Instruction> instrs, int index, OpCode opcode) {
			if (index < 0)
				return -1;
			for (int i = index; i < instrs.Count; i++) {
				if (instrs[i].OpCode == opcode)
					return i;
			}
			return -1;
		}

		static bool HasFieldType(IEnumerable<FieldDef> fields, TypeDef fieldType) {
			foreach (var field in fields) {
				if (new SigComparer().Equals(field.FieldSig.GetFieldType(), fieldType))
					return true;
			}
			return false;
		}

		static int GetOffsetMagic(MethodDef method) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				int index = i;

				var ldsfld1 = instrs[index++];
				if (ldsfld1.OpCode.Code != Code.Ldsfld)
					continue;

				var ldci4 = instrs[index++];
				if (!ldci4.IsLdcI4())
					continue;

				var callvirt = instrs[index++];
				if (callvirt.OpCode.Code != Code.Callvirt)
					continue;
				var calledMethod = callvirt.Operand as IMethod;
				if (calledMethod == null)
					continue;
				if (calledMethod.FullName != "System.Void System.Reflection.Emit.ILGenerator::Emit(System.Reflection.Emit.OpCode,System.Int32)")
					continue;

				if (!instrs[index++].IsLdloc())
					continue;

				var ldsfld2 = instrs[index++];
				if (ldsfld2.OpCode.Code != Code.Ldsfld)
					continue;
				var field = ldsfld2.Operand as IField;
				if (field == null)
					continue;
				if (field.FullName != "System.Reflection.Emit.OpCode System.Reflection.Emit.OpCodes::Xor")
					continue;

				// Here if Babel.NET 5.5
				return ldci4.GetLdcI4Value();
			}

			// Here if Babel.NET <= 5.0
			return 0;
		}

		bool CheckFields(TypeDef type, string fieldType1, TypeDef fieldType2) {
			if (type.Fields.Count != 2)
				return false;
			if (type.Fields[0].FieldSig.GetFieldType().GetFullName() != fieldType1 &&
				type.Fields[1].FieldSig.GetFieldType().GetFullName() != fieldType1)
				return false;
			if (!new SigComparer().Equals(type.Fields[0].FieldSig.GetFieldType(), fieldType2) &&
				!new SigComparer().Equals(type.Fields[1].FieldSig.GetFieldType(), fieldType2))
				return false;
			return true;
		}

		public void Initialize() {
			if (decrypterType == null)
				return;
			if (encryptedResource != null)
				return;

			if (decrypterInfo.NeedsResource) {
				encryptedResource = BabelUtils.FindEmbeddedResource(module, decrypterType);
				if (encryptedResource == null)
					return;
			}

			decrypterInfo.Initialize(module, encryptedResource);
		}

		public string Decrypt(object[] args) => decrypterInfo.Decrypt(args);
	}
}
