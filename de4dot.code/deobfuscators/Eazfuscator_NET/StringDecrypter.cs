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

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class StringDecrypter : IDisposable {
		ModuleDefMD module;
		TypeDef stringType;
		MethodDef stringMethod;
		TypeDef dataDecrypterType;
		short s1, s2, s3;
		int i1, /*i2,*/ i3, i4, i5, i6;
		bool checkMinus2;
		bool usePublicKeyToken;
		int keyLen;
		byte[] theKey;
		int magic1;
		uint rldFlag, bytesFlag;
		EmbeddedResource encryptedResource;
		BinaryReader reader;
		DecrypterType decrypterType;
		StreamHelperType streamHelperType;
		EfConstantsReader stringMethodConsts;
		bool isV32OrLater;
		bool isV50OrLater;
		bool isV51OrLater;
		int? validStringDecrypterValue;
		DynamicDynocodeIterator dynocode;
		MethodDef realMethod;

		class StreamHelperType {
			public TypeDef type;
			public MethodDef readInt16Method;
			public MethodDef readInt32Method;
			public MethodDef readBytesMethod;

			public bool Detected =>
				readInt16Method != null &&
				readInt32Method != null &&
				readBytesMethod != null;

			public StreamHelperType(TypeDef type) {
				this.type = type;

				foreach (var method in type.Methods) {
					if (method.IsStatic || method.Body == null || method.IsPrivate || method.GenericParameters.Count > 0)
						continue;
					if (DotNetUtils.IsMethod(method, "System.Int16", "()"))
						readInt16Method = method;
					else if (DotNetUtils.IsMethod(method, "System.Int32", "()"))
						readInt32Method = method;
					else if (DotNetUtils.IsMethod(method, "System.Byte[]", "(System.Int32)"))
						readBytesMethod = method;
				}
			}
		}

		public int? ValidStringDecrypterValue => validStringDecrypterValue;
		public TypeDef Type => stringType;
		public EmbeddedResource Resource => encryptedResource;
		public IEnumerable<TypeDef> Types => new List<TypeDef> { stringType, dataDecrypterType };
		public IEnumerable<TypeDef> DynocodeTypes => dynocode.Types;
		public MethodDef Method => stringMethod;
		public bool Detected => stringType != null;

		/// <summary>
		/// In 5.0, the actual string decrypter method doesn't do much, calls a helper method which
		/// does most of the work (and is mostly the same as the stringMethod from 4.9 and below).
		/// </summary>
		public bool HasRealMethod => realMethod != null;
		public MethodDef RealMethod => realMethod ?? stringMethod;

		public StringDecrypter(ModuleDefMD module, DecrypterType decrypterType) {
			this.module = module;
			this.decrypterType = decrypterType;
		}

		static bool CheckIfV32OrLater(TypeDef type) {
			int numInts = 0;
			foreach (var field in type.Fields) {
				if (field.FieldSig.GetFieldType().GetElementType() == ElementType.I4)
					numInts++;
			}
			return numInts >= 2;
		}

		public void Find() {
			foreach (var type in module.Types) {
				if (!CheckType(type))
					continue;

				foreach (var method in type.Methods) {
					if (!CheckDecrypterMethod(method))
						continue;

					// 5.0
					if (CheckIfHelperMethod(method)) {
						stringMethod = method;
						realMethod = GetRealDecrypterMethod(method);
						isV50OrLater = true;
						foreach (var inst in stringMethod.Body.Instructions) {
							if (inst.OpCode.Code == Code.Cgt_Un) {
								isV51OrLater = true;
								break;
							}
						}
					}
					else stringMethod = method;

					stringType = type;
					isV32OrLater = CheckIfV32OrLater(stringType);
					return;
				}
			}
		}

		static string[] requiredFieldTypes = new string[] {
			"System.Byte[]",
			"System.Int16",
		};
		bool CheckType(TypeDef type) {
			if (!new FieldTypes(type).All(requiredFieldTypes))
				return false;
			if (type.NestedTypes.Count == 0) {
				return DotNetUtils.FindFieldType(type, "System.IO.BinaryReader", true) != null &&
					DotNetUtils.FindFieldType(type, "System.Collections.Generic.Dictionary`2<System.Int32,System.String>", true) != null;
			}
			else if (type.NestedTypes.Count == 3) {
				streamHelperType = FindStreamHelperType(type);
				return streamHelperType != null;
			}
			else if (type.NestedTypes.Count == 1) {
				return type.NestedTypes[0].IsEnum;
			}
			else
				return false;
		}

		static string[] streamHelperTypeFields = new string[] {
			"System.IO.Stream",
			"System.Byte[]",
		};
		static StreamHelperType FindStreamHelperType(TypeDef type) {
			foreach (var field in type.Fields) {
				var nested = field.FieldSig.GetFieldType().TryGetTypeDef();
				if (nested == null)
					continue;
				if (nested.DeclaringType != type)
					continue;
				if (!new FieldTypes(nested).Exactly(streamHelperTypeFields))
					continue;
				var streamHelperType = new StreamHelperType(nested);
				if (!streamHelperType.Detected)
					continue;

				return streamHelperType;
			}
			return null;
		}

		static string[] requiredLocalTypes = new string[] {
			"System.Boolean",
			"System.Byte[]",
			"System.Char[]",
			"System.Int16",
			"System.Int32",
			"System.Reflection.Assembly",
			"System.String",
		};
		static bool CheckDecrypterMethod(MethodDef method) {
			if (method == null || !method.IsStatic || method.Body == null)
				return false;
			if (!(DotNetUtils.IsMethod(method, "System.String", "(System.Int32)") || DotNetUtils.IsMethod(method, "System.String", "(System.Int32,System.Boolean)")))
				return false;
			if (!new LocalTypes(method).All(requiredLocalTypes))
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode != OpCodes.Callvirt)
					continue;
				if (instr.Operand is IMethod calledMethod && calledMethod.FullName == "System.IO.Stream System.Reflection.Assembly::GetManifestResourceStream(System.String)")
					return true;
			}

			return false;
		}

		/// <remarks>5.0</remarks>
		static bool CheckIfHelperMethod(MethodDef method) {
			// Helper method will be `private static`, instead of `internal static`
			return method.IsPrivate;
		}

		/// <summary>
		/// Get the real decrypter method from a found helper method.
		/// </summary>
		/// <remarks>5.0</remarks>
		static MethodDef GetRealDecrypterMethod(MethodDef helper) {
			var methods = helper.DeclaringType.Methods;
			foreach (var method in methods) {
				if (method.MDToken != helper.MDToken &&
					method.IsAssembly &&
					method.Parameters.Count >= 1 &&
					method.Parameters[0].Type == helper.Parameters[0].Type)	//checking first type, which should be string
					return method;
			}

			return null;
		}

		public void Initialize(ISimpleDeobfuscator simpleDeobfuscator) {
			if (stringType == null)
				return;

			if (!FindConstants(simpleDeobfuscator)) {
				if (encryptedResource == null)
					Logger.w("Could not find encrypted resource. Strings cannot be decrypted.");
				else
					Logger.w("Can't decrypt strings. Possibly a new Eazfuscator.NET version.");
				return;
			}
		}

		bool FindConstants(ISimpleDeobfuscator simpleDeobfuscator) {
			dynocode = new DynamicDynocodeIterator();
			simpleDeobfuscator.Deobfuscate(stringMethod);
			stringMethodConsts = new EfConstantsReader(stringMethod);

			if (!FindResource(stringMethod))
				return false;

			checkMinus2 = isV32OrLater || DeobUtils.HasInteger(stringMethod, -2);
			usePublicKeyToken = CallsGetPublicKeyToken(stringMethod);

			var int64Method = FindInt64Method(stringMethod);
			if (int64Method != null)
				decrypterType.Type = int64Method.DeclaringType;

			if (!FindShorts())
				return false;
			if (!FindInt3())
				return false;
			if (!FindInt4())
				return false;
			if (checkMinus2 && !FindInt5())
				return false;

			// The method body of the data decrypter method has been moved into
			// the string decrypter helper method in 5.0
			if (!isV50OrLater) {
				dataDecrypterType = FindDataDecrypterType(stringMethod);
				if (dataDecrypterType == null)
					return false;
			}

			if (isV32OrLater) {
				int index = FindInitIntsIndex(stringMethod, out bool initializedAll);

				//better return early than late on error
				if (index == -1)
					return false;

				var cctor = stringType.FindStaticConstructor();
				if (!initializedAll && cctor != null) {
					simpleDeobfuscator.Deobfuscate(cctor);
					if (!FindIntsCctor(cctor))
						return false;
				}

				if (decrypterType.Detected && !decrypterType.Initialize())
					return false;

				if (!isV50OrLater)
					decrypterType.ShiftConsts = new List<int> { 24, 16, 8, 0, 16, 8, 0, 24 };
				else {
					if (!FindShiftInts(decrypterType.Int64Method, out var shiftConsts))
						return false;

					decrypterType.ShiftConsts = shiftConsts;
				}

				if (!FindInts(index))
					return false;
			}


			InitializeFlags();
			Initialize();

			return true;
		}

		void InitializeFlags() {
			if (!isV32OrLater) {
				rldFlag = 0x40000000;
				bytesFlag = 0x80000000;
				return;
			}

			var instrs = stringMethod.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var ldci4 = instrs[i];
				if (!stringMethodConsts.IsLoadConstantInt32(ldci4))
					continue;
				int index = i;
				if (!stringMethodConsts.GetInt32(ref index, out int tmp) || !IsFlagsMask(tmp))
					continue;
				if (FindFlags(i))
					return;
			}

			throw new ApplicationException("Could not find string decrypter flags");
		}

		static bool IsFlagsMask(int value) => value == 0x1FFFFFFF || value == 0x0FFFFFFF;

		class FlagsInfo {
			public Local Local { get; set; }
			public uint Value { get; set; }
			public int Offset { get; set; }
			public FlagsInfo(Local local, uint value, int offset) {
				Local = local;
				Value = value;
				Offset = offset;
			}
		}

		bool FindFlags(int index) {
			var flags = FindFlags2(index);
			if (flags == null)
				return false;

			flags.Sort((a, b) => a.Offset.CompareTo(b.Offset));

			rldFlag = flags[0].Value;
			bytesFlag = flags[1].Value;
			return true;
		}

		List<FlagsInfo> FindFlags2(int index) {
			var flags = new List<FlagsInfo>(3);
			for (int i = index - 1; i >= 0; i--) {
				var instr = stringMethod.Body.Instructions[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					break;
				if (!stringMethodConsts.IsLoadConstantInt32(instr))
					continue;
				int index2 = i;
				if (!stringMethodConsts.GetInt32(ref index2, out int value))
					continue;
				if ((uint)value != 0x80000000 && value != 0x40000000 && value != 0x20000000)
					continue;
				var local = GetFlagsLocal(stringMethod, index2);
				if (local == null)
					continue;
				int offset = GetFlagsOffset(stringMethod, index2, local);
				if (offset < 0)
					continue;

				flags.Add(new FlagsInfo(local, (uint)value, offset));
				if (flags.Count != 3)
					continue;

				return flags;
			}

			return null;
		}

		static int GetFlagsOffset(MethodDef method, int index, Local local) {
			var instrs = method.Body.Instructions;
			for (; index < instrs.Count; index++) {
				var ldloc = instrs[index];
				if (!ldloc.IsLdloc())
					continue;
				if (ldloc.GetLocal(method.Body.Variables) != local)
					continue;

				return index;
			}
			return -1;
		}

		Local GetFlagsLocal(MethodDef method, int index) {
			if (isV51OrLater)
				return GetFlagsLocalNew(method, index);
			return GetFlagsLocalOld(method, index);
		}

		// <= 5.0 
		static Local GetFlagsLocalOld(MethodDef method, int index) {
			var instrs = method.Body.Instructions;
			if (index + 5 >= instrs.Count)
				return null;
			if (instrs[index++].OpCode.Code != Code.And)
				return null;
			if (instrs[index++].OpCode.Code != Code.Ldc_I4_0)
				return null;
			if (instrs[index++].OpCode.Code != Code.Ceq)
				return null;
			if (instrs[index++].OpCode.Code != Code.Ldc_I4_0)
				return null;
			if (instrs[index++].OpCode.Code != Code.Ceq)
				return null;
			var stloc = instrs[index++];
			if (!stloc.IsStloc())
				return null;
			return stloc.GetLocal(method.Body.Variables);
		}

		// 5.1+
		// Uses different OpCodes
		static Local GetFlagsLocalNew(MethodDef method, int index) {
			var instrs = method.Body.Instructions;
			if (index + 5 >= instrs.Count)
				return null;
			if (instrs[index++].OpCode.Code != Code.And)
				return null;
			if (instrs[index++].OpCode.Code != Code.Ldc_I4_0)
				return null;
			if (instrs[index++].OpCode.Code != Code.Cgt_Un)
				return null;
			var stloc = instrs[index++];
			if (!stloc.IsStloc())
				return null;
			return stloc.GetLocal(method.Body.Variables);
		}

		void Initialize() {
			reader = new BinaryReader(encryptedResource.CreateReader().AsStream());
			short len = (short)(reader.ReadInt16() ^ s1);
			if (len != 0)
				theKey = reader.ReadBytes(len);
			else
				keyLen = reader.ReadInt16() ^ s2;
		}

		public string Decrypt(int val) {
			validStringDecrypterValue = val;
			while (true) {
				int offset = magic1 ^ i3 ^ val ^ i6;
				reader.BaseStream.Position = offset;
				byte[] tmpKey;
				if (theKey == null) {
					tmpKey = reader.ReadBytes(keyLen == -1 ? (short)(reader.ReadInt16() ^ s3 ^ offset) : keyLen);
					if (isV32OrLater) {
						for (int i = 0; i < tmpKey.Length; i++)
							tmpKey[i] ^= (byte)(magic1 >> ((i & 3) << 3));
					}
				}
				else
					tmpKey = theKey;

				int flags = i4 ^ magic1 ^ offset ^ reader.ReadInt32();
				if (checkMinus2 && flags == -2) {
					var ary2 = reader.ReadBytes(4);
					val = -(magic1 ^ i5) ^ (ary2[2] | (ary2[0] << 8) | (ary2[3] << 16) | (ary2[1] << 24));
					continue;
				}

				var bytes = reader.ReadBytes(flags & 0x1FFFFFFF);
				Decrypt1(bytes, tmpKey);
				var pkt = PublicKeyBase.ToPublicKeyToken(module.Assembly.PublicKey);
				if (usePublicKeyToken && !PublicKeyBase.IsNullOrEmpty2(pkt)) {
					for (int i = 0; i < bytes.Length; i++)
						bytes[i] ^= (byte)((pkt.Data[i & 7] >> 5) + (pkt.Data[i & 7] << 3));
				}

				if ((flags & rldFlag) != 0)
					bytes = rld(bytes);
				if ((flags & bytesFlag) != 0) {
					var sb = new StringBuilder(bytes.Length);
					foreach (var b in bytes)
						sb.Append((char)b);
					return sb.ToString();
				}
				else
					return Encoding.Unicode.GetString(bytes);
			}
		}

		static byte[] rld(byte[] src) {
			var dst = new byte[src[2] + (src[3] << 8) + (src[0] << 16) + (src[1] << 24)];
			int srcIndex = 4;
			int dstIndex = 0;
			int flags = 0;
			int bit = 128;
			while (dstIndex < dst.Length) {
				bit <<= 1;
				if (bit == 256) {
					bit = 1;
					flags = src[srcIndex++];
				}

				if ((flags & bit) == 0) {
					dst[dstIndex++] = src[srcIndex++];
					continue;
				}

				int numBytes = (src[srcIndex] >> 2) + 3;
				int copyIndex = dstIndex - ((src[srcIndex + 1] + (src[srcIndex] << 8)) & 0x3FF);
				if (copyIndex < 0)
					break;
				while (dstIndex < dst.Length && numBytes-- > 0)
					dst[dstIndex++] = dst[copyIndex++];
				srcIndex += 2;
			}

			return dst;
		}

		static void Decrypt1(byte[] dest, byte[] key) {
			byte b = (byte)((key[1] + 7) ^ (dest.Length + 11));
			uint lcg = (uint)((key[0] | (key[2] << 8)) + (b << 3));
			b += 3;
			ushort xn = 0;
			for (int i = 0; i < dest.Length; i++) {
				if ((i & 1) == 0) {
					lcg = LcgNext(lcg);
					xn = (ushort)(lcg >> 16);
				}
				byte tmp = dest[i];
				dest[i] ^= (byte)(key[1] ^ xn ^ b);
				b = (byte)(tmp + 3);
				xn >>= 8;
			}
		}

		static uint LcgNext(uint lcg) => lcg * 214013 + 2531011;

		bool FindResource(MethodDef method) {
			encryptedResource = FindResourceFromCodeString(method) ??
								FindResourceFromStringBuilder(method);
			return encryptedResource != null;
		}

		EmbeddedResource FindResourceFromCodeString(MethodDef method) =>
			DotNetUtils.GetResource(module, DotNetUtils.GetCodeStrings(method)) as EmbeddedResource;

		EmbeddedResource FindResourceFromStringBuilder(MethodDef method) {
			int startIndex = EfUtils.FindOpCodeIndex(method, 0, Code.Newobj, "System.Void System.Text.StringBuilder::.ctor()");
			if (startIndex < 0)
				return null;
			int endIndex = EfUtils.FindOpCodeIndex(method, startIndex, Code.Call, "System.String System.Text.StringBuilder::ToString()");
			if (endIndex < 0)
				return null;

			var sb = new StringBuilder();
			var instrs = method.Body.Instructions;
			int val = 0, shift = 0;
			for (int i = startIndex; i < endIndex; i++) {
				var instr = instrs[i];
				if (instr.OpCode.Code == Code.Call && instr.Operand.ToString() == "System.Text.StringBuilder System.Text.StringBuilder::Append(System.Char)") {
					sb.Append((char)(val >> shift));
					shift = 0;
				}
				if (stringMethodConsts.IsLoadConstantInt32(instr)) {
					if (!stringMethodConsts.GetInt32(ref i, out int tmp))
						break;
					if (i >= endIndex)
						break;

					var next = instrs[i];
					if (next.OpCode.Code == Code.Shr)
						shift = tmp;
					else {
						val = tmp;
						shift = 0;
					}
				}
			}

			return DotNetUtils.GetResource(module, sb.ToString()) as EmbeddedResource;
		}

		bool FindShiftInts(MethodDef method, out List<int> bytes) {
			var instrs = method.Body.Instructions;
			var constantsReader = new EfConstantsReader(method);
			bytes = new List<int>(8);

			for (int i = 0; i < instrs.Count - 4; i++) {
				if (bytes.Count >= 8)
					return true;

				var ldloc1 = instrs[i];
				if (ldloc1.OpCode.Code != Code.Ldloc_1)
					continue;

				var ldlocs = instrs[i + 1];
				if (ldlocs.OpCode.Code != Code.Ldloc_S)
					continue;

				var maybe = instrs[i + 2];
				if (maybe.OpCode.Code == Code.Conv_U1) {
					var callvirt = instrs[i + 3];
					if (callvirt.OpCode.Code != Code.Callvirt)
						return false;

					bytes.Add(0);
					continue;
				}
				var shr = instrs[i + 3];
				if (shr.OpCode.Code != Code.Shr)
					return false;

				var convu1 = instrs[i + 4];
				if (convu1.OpCode.Code != Code.Conv_U1)
					return false;

				int index = i + 2;
				if (!constantsReader.GetInt32(ref index, out int constant))
					return false;

				bytes.Add(constant);
			}

			return false;
		}

		static MethodDef FindInt64Method(MethodDef method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Int64", "()"))
					continue;

				return calledMethod;
			}
			return null;
		}

		static TypeDef FindDataDecrypterType(MethodDef method) {
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDef;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.IsMethod(calledMethod, "System.Byte[]", "(System.Byte[],System.Byte[])"))
					continue;

				return calledMethod.DeclaringType;
			}
			return null;
		}

		bool FindShorts() {
			int index = 0;
			if (!FindShort(ref index, ref s1))
				return false;
			if (!FindShort(ref index, ref s2))
				return false;
			if (!FindShort(ref index, ref s3))
				return false;

			return true;
		}

		bool FindShort(ref int index, ref short s) {
			if (!FindCallReadInt16(ref index))
				return false;
			index++;
			return stringMethodConsts.GetInt16(ref index, out s);
		}

		bool FindInts(int index) {
			if (index < 0)
				return false;

			//i2 = 0;
			var instrs = stringMethod.Body.Instructions;

			var emu = new InstructionEmulator(stringMethod);
			foreach (var kv in stringMethodConsts.Locals32)
				emu.SetLocal(kv.Key, new Int32Value(kv.Value));

			var fields = new Dictionary<FieldDef, int?>();
			for (int i = index; i < instrs.Count - 2; i++) {
				var instr = instrs[i];

				FieldDef field;
				switch (instr.OpCode.Code) {
				case Code.Ldsfld:
					field = instr.Operand as FieldDef;
					if (field == null || field.DeclaringType != stringMethod.DeclaringType || field.FieldType.GetElementType() != ElementType.I4)
						goto default;
					fields[field] = null;
					emu.Push(new Int32Value(i1));
					break;

				case Code.Stsfld:
					field = instr.Operand as FieldDef;
					if (field == null || field.DeclaringType != stringMethod.DeclaringType || field.FieldType.GetElementType() != ElementType.I4)
						goto default;
					if (fields.ContainsKey(field) && fields[field] == null)
						goto default;
					var val = emu.Pop() as Int32Value;
					if (val == null || !val.AllBitsValid())
						fields[field] = null;
					else
						fields[field] = val.Value;
					break;

				case Code.Call:
					var method = instr.Operand as MethodDef;
					if (!decrypterType.Detected || method != decrypterType.Int64Method)
						goto done;
					emu.Push(new Int64Value((long)decrypterType.GetMagic()));
					break;

				case Code.Newobj:
					if (!EmulateDynocode(emu, ref i))
						goto default;
					break;

				default:
					if (instr.OpCode.FlowControl != FlowControl.Next)
						goto done;
					emu.Emulate(instr);
					break;
				}
			}
done:

			foreach (var val in fields.Values) {
				if (val == null)
					continue;
				magic1 = /*i2 =*/ val.Value;
				return true;
			}

			return false;
		}

		bool EmulateDynocode(InstructionEmulator emu, ref int index) {
			if (isV51OrLater)
				return EmulateDynocodeNew(emu, ref index);
			return EmulateDynocodeOld(emu, ref index);
		}

		// <= 5.0
		bool EmulateDynocodeOld(InstructionEmulator emu, ref int index) {
			var instrs = stringMethod.Body.Instructions;
			var instr = instrs[index];

			var ctor = instr.Operand as MethodDef;
			if (ctor == null || ctor.MethodSig.GetParamCount() != 1 || ctor.MethodSig.Params[0].ElementType != ElementType.I4)
				return false;

			if (index + 4 >= instrs.Count)
				return false;
			var ldloc = instrs[index + 3];
			var stfld = instrs[index + 4];
			if (!ldloc.IsLdloc() || stfld.OpCode.Code != Code.Stfld)
				return false;
			var enumerableField = stfld.Operand as FieldDef;
			if (enumerableField == null)
				return false;

			var initValue = emu.GetLocal(ldloc.GetLocal(stringMethod.Body.Variables)) as Int32Value;
			if (initValue == null || !initValue.AllBitsValid())
				return false;

			int leaveIndex = FindLeave(instrs, index);
			if (leaveIndex < 0)
				return false;
			var afterLoop = instrs[leaveIndex].Operand as Instruction;
			if (afterLoop == null)
				return false;
			int newIndex = instrs.IndexOf(afterLoop);
			var loopLocal = GetDCLoopLocal(index, newIndex);
			if (loopLocal == null)
				return false;
			var initValue2 = emu.GetLocal(loopLocal) as Int32Value;
			if (initValue2 == null || !initValue2.AllBitsValid())
				return false;

			int loopStart = GetIndexOfCall(instrs, index, leaveIndex, "System.Int32", "()");
			int loopEnd = GetIndexOfCall(instrs, loopStart, leaveIndex, "System.Boolean", "()");
			if (loopStart < 0 || loopEnd < 0)
				return false;
			loopStart++;
			loopEnd--;

			dynocode.Initialize(module);
			var ctorArg = emu.Pop() as Int32Value;
			if (ctorArg == null || !ctorArg.AllBitsValid())
				return false;
			dynocode.CreateEnumerable(ctor, new object[] { ctorArg.Value });
			dynocode.WriteEnumerableField(enumerableField.MDToken.ToUInt32(), initValue.Value);
			dynocode.CreateEnumerator();
			foreach (var val in dynocode) {
				emu.Push(new Int32Value(val));
				for (int i = loopStart; i < loopEnd; i++)
					emu.Emulate(instrs[i]);
			}

			index = newIndex - 1;
			return true;
		}

		// 5.1+
		// the only changes are the indexes of ldloc and stfld
		bool EmulateDynocodeNew(InstructionEmulator emu, ref int index) {
			var instrs = stringMethod.Body.Instructions;
			var instr = instrs[index];

			var ctor = instr.Operand as MethodDef;
			if (ctor == null || ctor.MethodSig.GetParamCount() != 1 || ctor.MethodSig.Params[0].ElementType != ElementType.I4)
				return false;

			if (index + 4 >= instrs.Count)
				return false;
			var ldloc = instrs[index + 2];
			var stfld = instrs[index + 3];
			if (!ldloc.IsLdloc() || stfld.OpCode.Code != Code.Stfld)
				return false;
			var enumerableField = stfld.Operand as FieldDef;
			if (enumerableField == null)
				return false;

			var initValue = emu.GetLocal(ldloc.GetLocal(stringMethod.Body.Variables)) as Int32Value;
			if (initValue == null || !initValue.AllBitsValid())
				return false;

			int leaveIndex = FindLeave(instrs, index);
			if (leaveIndex < 0)
				return false;
			var afterLoop = instrs[leaveIndex].Operand as Instruction;
			if (afterLoop == null)
				return false;
			int newIndex = instrs.IndexOf(afterLoop);
			var loopLocal = GetDCLoopLocal(index, newIndex);
			if (loopLocal == null)
				return false;
			var initValue2 = emu.GetLocal(loopLocal) as Int32Value;
			if (initValue2 == null || !initValue2.AllBitsValid())
				return false;

			int loopStart = GetIndexOfCall(instrs, index, leaveIndex, "System.Int32", "()");
			int loopEnd = GetIndexOfCall(instrs, loopStart, leaveIndex, "System.Boolean", "()");
			if (loopStart < 0 || loopEnd < 0)
				return false;
			loopStart++;
			loopEnd--;

			dynocode.Initialize(module);
			var ctorArg = emu.Pop() as Int32Value;
			if (ctorArg == null || !ctorArg.AllBitsValid())
				return false;
			dynocode.CreateEnumerable(ctor, new object[] { ctorArg.Value });
			dynocode.WriteEnumerableField(enumerableField.MDToken.ToUInt32(), initValue.Value);
			dynocode.CreateEnumerator();
			foreach (var val in dynocode) {
				emu.Push(new Int32Value(val));
				for (int i = loopStart; i < loopEnd; i++)
					emu.Emulate(instrs[i]);
			}

			index = newIndex - 1;
			return true;
		}

		static int GetIndexOfCall(IList<Instruction> instrs, int startIndex, int endIndex, string returnType, string parameters) {
			if (startIndex < 0 || endIndex < 0)
				return -1;
			for (int i = startIndex; i < endIndex; i++) {
				var instr = instrs[i];
				if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
					continue;
				var method = instr.Operand as IMethod;
				if (!DotNetUtils.IsMethod(method, returnType, parameters))
					continue;

				return i;
			}
			return -1;
		}

		Local GetDCLoopLocal(int start, int end) {
			var instrs = stringMethod.Body.Instructions;
			for (int i = start; i < end - 1; i++) {
				if (instrs[i].OpCode.Code != Code.Xor)
					continue;
				var stloc = instrs[i + 1];
				if (!stloc.IsStloc())
					continue;
				return stloc.GetLocal(stringMethod.Body.Variables);
			}
			return null;
		}

		static int FindLeave(IList<Instruction> instrs, int index) {
			for (int i = index; i < instrs.Count; i++) {
				if (instrs[i].OpCode.Code == Code.Leave_S || instrs[i].OpCode.Code == Code.Leave)
					return i;
			}
			return -1;
		}

		static int FindInitIntsIndex(MethodDef method, out bool initializedAll) {
			initializedAll = false;

			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var ldnull = instrs[i];
				if (ldnull.OpCode.Code != Code.Ldnull
					&& ldnull.OpCode.Code != Code.Call)
					continue;

				var stsfld = instrs[i + 1];
				if (stsfld.OpCode.Code != Code.Stsfld)
					continue;

				var storeField = stsfld.Operand as FieldDef;
				if (storeField == null || storeField.FieldType.FullName != "System.Byte[]")
					continue;

				var instr = instrs[i + 2];
				if (instr.OpCode.Code == Code.Ldsfld) {
					var loadField = instr.Operand as FieldDef;
					if (loadField == null || loadField.FieldType.GetElementType() != ElementType.I4)
						continue;
				}
				else if (instr.IsLdcI4()) {
					initializedAll = true;
				}
				else
					continue;

				return i + 2;	//+2 or else we would land on the call method
			}

			return -1;
		}

		bool FindIntsCctor(MethodDef cctor) {
			int index = 0;

			//since somewhere after eaz 5.2, there are 2 calls to GetFrame, we need the last one
			if (!FindLastCallGetFrame(cctor, ref index))
				return FindIntsCctor2(cctor);

			int tmp3 = 0;
			var constantsReader = new EfConstantsReader(cctor);
			if (!constantsReader.GetNextInt32(ref index, out int tmp1))
				return false;
			if (tmp1 == 0 && !constantsReader.GetNextInt32(ref index, out tmp1))
				return false;
			if (!constantsReader.GetNextInt32(ref index, out int tmp2))
				return false;
			if (tmp2 == 0 && !constantsReader.GetNextInt32(ref index, out tmp2))
				return false;

			index = 0;
			var instrs = cctor.Body.Instructions;
			while (index < instrs.Count) {
				if (!constantsReader.GetNextInt32(ref index, out int tmp4))
					break;
				if (index < instrs.Count && instrs[index].IsLdloc())
					tmp3 = tmp4;
			}

			i1 = tmp1 ^ tmp2 ^ tmp3;
			return true;
		}

		// Compact Framework doesn't have StackFrame
		bool FindIntsCctor2(MethodDef cctor) {
			int index = 0;
			var instrs = cctor.Body.Instructions;
			var constantsReader = new EfConstantsReader(cctor);
			while (index >= 0) {
				if (!constantsReader.GetNextInt32(ref index, out int val))
					break;
				if (index < instrs.Count && instrs[index].OpCode.Code == Code.Add) {
					i1 = val;
					return true;
				}
			}

			return false;
		}

		bool FindInt3() {
			if (!isV32OrLater)
				return FindInt3Old();
			return FindInt3New();
		}

		// <= 3.1
		bool FindInt3Old() {
			var instrs = stringMethod.Body.Instructions;
			for (int i = 0; i < instrs.Count - 4; i++) {
				var ldarg0 = instrs[i];
				if (ldarg0.OpCode.Code != Code.Ldarg_0)
					continue;

				var ldci4 = instrs[i + 1];
				if (!ldci4.IsLdcI4())
					continue;

				int index = i + 1;
				if (!stringMethodConsts.GetInt32(ref index, out int value))
					continue;
				if (index >= instrs.Count)
					continue;

				if (instrs[index].OpCode.Code != Code.Xor)
					continue;

				i3 = value;
				return true;
			}

			return false;
		}

		// 3.2+
		bool FindInt3New() {
			var instrs = stringMethod.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = i;

				var ldarg0 = instrs[index++];
				if (ldarg0.OpCode.Code != Code.Ldarg_0)
					continue;

				if (!stringMethodConsts.GetInt32(ref index, out int value))
					continue;

				if (index + 3 >= instrs.Count)
					break;

				if (instrs[index++].OpCode.Code != Code.Xor)
					continue;

				if (!instrs[index++].IsLdloc())
					continue;

				if (instrs[index++].OpCode.Code != Code.Xor)
					continue;

				i3 = value;
				if (!FindInt6(index++))
					return false;
				return true;
			}

			return false;
		}

		// v3.3.134+
		bool FindInt6(int index) {
			index = GetNextLdci4InSameBlock(index);
			if (index < 0)
				return true;

			return stringMethodConsts.GetNextInt32(ref index, out i6);
		}

		bool FindInt4() {
			int index = 0;
			if (!FindCallReadInt32(ref index))
				return false;
			if (!stringMethodConsts.GetNextInt32(ref index, out i4))
				return false;

			return true;
		}

		int GetNextLdci4InSameBlock(int index) {
			var instrs = stringMethod.Body.Instructions;
			for (int i = index; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					return -1;
				if (stringMethodConsts.IsLoadConstantInt32(instr))
					return i;
			}

			return -1;
		}

		bool FindInt5() {
			int index = -1;
			while (true) {
				index++;
				if (!FindCallReadBytes(ref index))
					return false;
				if (index <= 0)
					continue;
				var ldci4 = stringMethod.Body.Instructions[index - 1];
				if (!ldci4.IsLdcI4())
					continue;
				if (ldci4.GetLdcI4Value() != 4)
					continue;
				if (!stringMethodConsts.GetNextInt32(ref index, out i5))
					return false;

				return true;
			}
		}

		static bool CallsGetPublicKeyToken(MethodDef method) {
			int index = 0;
			return FindCall(method, ref index, "System.Byte[] System.Reflection.AssemblyName::GetPublicKeyToken()");
		}

		bool FindCallReadInt16(ref int index) =>
			FindCall(stringMethod, ref index, streamHelperType == null ? "System.Int16 System.IO.BinaryReader::ReadInt16()" : streamHelperType.readInt16Method.FullName);

		bool FindCallReadInt32(ref int index) =>
			FindCall(stringMethod, ref index, streamHelperType == null ? "System.Int32 System.IO.BinaryReader::ReadInt32()" : streamHelperType.readInt32Method.FullName);

		bool FindCallReadBytes(ref int index) =>
			FindCall(stringMethod, ref index, streamHelperType == null ? "System.Byte[] System.IO.BinaryReader::ReadBytes(System.Int32)" : streamHelperType.readBytesMethod.FullName);

		static bool FindLastCallGetFrame(MethodDef method, ref int index) =>
			FindLastCall(method, ref index, "System.Diagnostics.StackFrame System.Diagnostics.StackTrace::GetFrame(System.Int32)");

		static bool FindLastCall(MethodDef method, ref int index, string methodFullName) {
			bool found;
			bool foundOnce = false;
			int tempIndex = index;

			//keep doing until findcall returns false (we reached the end of the method)
			do {
				found = FindCall(method, ref tempIndex, methodFullName);

				//indicate we did find one
				if (found) {
					foundOnce = true;
					index = tempIndex;

					//to not get stuck on the same instruction
					tempIndex++;
				}
			} while (found);
			return foundOnce;
		}

		static bool FindCall(MethodDef method, ref int index, string methodFullName) {
			for (; index < method.Body.Instructions.Count; index++) {
				if (!FindCallvirt(method, ref index))
					return false;

				var calledMethod = method.Body.Instructions[index].Operand as IMethod;
				if (calledMethod == null)
					continue;
				if (calledMethod.ToString() != methodFullName)
					continue;

				return true;
			}
			return false;
		}

		static bool FindCallvirt(MethodDef method, ref int index) {
			var instrs = method.Body.Instructions;
			for (; index < instrs.Count; index++) {
				var instr = instrs[index];
				if (instr.OpCode.Code != Code.Callvirt)
					continue;

				return true;
			}

			return false;
		}

		public void Dispose() => CloseServer();

		public void CloseServer() {
			if (dynocode != null)
				dynocode.Dispose();
			dynocode = null;
		}
	}
}
