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
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Confuser {
	class StringDecrypter {
		ModuleDefinition module;
		MethodDefinition decryptMethod;
		EmbeddedResource resource;
		uint magic1, magic2;
		BinaryReader reader;
		ConfuserVersion version = ConfuserVersion.Unknown;
		Decrypter decrypter;

		enum ConfuserVersion {
			Unknown,
			v10_r42915,
			v10_r48832,
			v11_r49299,
		}

		abstract class Decrypter {
			protected StringDecrypter stringDecrypter;

			protected Decrypter(StringDecrypter stringDecrypter) {
				this.stringDecrypter = stringDecrypter;
			}

			public abstract string decrypt(MethodDefinition caller, int magic);
		}

		class Decrypter_v10_r42915 : Decrypter {
			public Decrypter_v10_r42915(StringDecrypter stringDecrypter)
				: base(stringDecrypter) {
			}

			public override string decrypt(MethodDefinition caller, int magic) {
				var reader = stringDecrypter.reader;
				reader.BaseStream.Position = (caller.MetadataToken.ToInt32() ^ magic) - stringDecrypter.magic1;
				int len = reader.ReadInt32() ^ (int)~stringDecrypter.magic2;
				var bytes = reader.ReadBytes(len);
				var rand = new Random(caller.MetadataToken.ToInt32());

				int mask = 0;
				for (int i = 0; i < bytes.Length; i++) {
					byte b = bytes[i];
					bytes[i] = (byte)(b ^ (rand.Next() & mask));
					mask += b;
				}
				return Encoding.UTF8.GetString(bytes);
			}
		}

		class Decrypter_v10_r48832 : Decrypter {
			PolyConstantsReader constReader;

			public Decrypter_v10_r48832(StringDecrypter stringDecrypter)
				: base(stringDecrypter) {
			}

			public override string decrypt(MethodDefinition caller, int magic) {
				var reader = stringDecrypter.reader;
				reader.BaseStream.Position = (caller.MetadataToken.ToInt32() ^ magic) - stringDecrypter.magic1;
				int len = reader.ReadInt32() ^ (int)~stringDecrypter.magic2;
				var rand = new Random(caller.MetadataToken.ToInt32());

				var instrs = stringDecrypter.decryptMethod.Body.Instructions;
				constReader = new PolyConstantsReader(instrs, false);
				int polyIndex = ConfuserUtils.findCallMethod(instrs, 0, Code.Callvirt, "System.Int64 System.IO.BinaryReader::ReadInt64()");
				if (polyIndex < 0)
					throw new ApplicationException("Could not find start of decrypt code");

				var decrypted = new byte[len];
				for (int i = 0; i < len; i += 8) {
					constReader.Arg = reader.ReadInt64();
					int index = polyIndex;
					long val;
					if (!constReader.getInt64(ref index, out val) || instrs[index].OpCode.Code != Code.Conv_I8)
						throw new ApplicationException("Could not get string int64 value");
					Array.Copy(BitConverter.GetBytes(val ^ rand.Next()), 0, decrypted, i, Math.Min(8, len - i));
				}

				return Encoding.Unicode.GetString(decrypted);
			}
		}

		class Decrypter_v11_r49299 : Decrypter {
			MyConstantsReader constReader;

			class MyConstantsReader : ConstantsReader {
				long arg;
				bool firstTime;

				public long Arg {
					get { return arg; }
					set {
						arg = value;
						firstTime = true;
					}
				}

				public MyConstantsReader(IList<Instruction> instrs, bool emulateConvInstrs)
					: base(instrs, emulateConvInstrs) {
				}

				protected override bool processInstructionInt64(ref int index, Stack<ConstantInfo<long>> stack) {
					if (!firstTime)
						return false;
					firstTime = false;
					if (instructions[index].OpCode.Code != Code.Conv_I8)
						return false;

					stack.Push(new ConstantInfo<long>(index, arg));
					index = index + 1;
					return true;
				}
			}

			public Decrypter_v11_r49299(StringDecrypter stringDecrypter)
				: base(stringDecrypter) {
			}

			public override string decrypt(MethodDefinition caller, int magic) {
				var reader = stringDecrypter.reader;
				reader.BaseStream.Position = (caller.MetadataToken.ToInt32() ^ magic) - stringDecrypter.magic1;
				int len = reader.ReadInt32() ^ (int)~stringDecrypter.magic2;
				var decrypted = new byte[len];

				int startIndex, endIndex;
				if (!findPolyStartEndIndexes(out startIndex, out endIndex))
					throw new ApplicationException("Could not get start/end indexes");

				constReader = new MyConstantsReader(stringDecrypter.decryptMethod.Body.Instructions, false);
				for (int i = 0; i < len; i++) {
					constReader.Arg = Utils.readEncodedInt32(reader);
					int index = startIndex;
					long result;
					if (!constReader.getInt64(ref index, out result) || index != endIndex)
						throw new ApplicationException("Could not decrypt integer");
					decrypted[i] = (byte)result;
				}

				return Encoding.Unicode.GetString(decrypted);
			}

			bool findPolyStartEndIndexes(out int startIndex, out int endIndex) {
				startIndex = 0;
				endIndex = 0;

				var local = findLocal(stringDecrypter.decryptMethod);
				if (local == null)
					return false;

				if ((endIndex = findEndIndex(stringDecrypter.decryptMethod)) < 0)
					return false;

				if ((startIndex = findStartIndex(stringDecrypter.decryptMethod, endIndex)) < 0)
					return false;

				return true;
			}

			static VariableDefinition findLocal(MethodDefinition method) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 3; i++) {
					if (instrs[i].OpCode.Code != Code.And)
						continue;
					if (instrs[i + 1].OpCode.Code != Code.Shl)
						continue;
					if (instrs[i + 2].OpCode.Code != Code.Or)
						continue;
					if (!DotNetUtils.isStloc(instrs[i + 3]))
						continue;
					return DotNetUtils.getLocalVar(method.Body.Variables, instrs[i + 3]);
				}
				return null;
			}

			static int findEndIndex(MethodDefinition method) {
				var instrs = method.Body.Instructions;
				for (int i = 0; i < instrs.Count - 5; i++) {
					if (instrs[i].OpCode.Code != Code.Conv_U1)
						continue;
					if (instrs[i + 1].OpCode.Code != Code.Stelem_I1)
						continue;
					if (!DotNetUtils.isLdloc(instrs[i + 2]))
						continue;
					if (!DotNetUtils.isLdcI4(instrs[i + 3]))
						continue;
					if (instrs[i + 4].OpCode.Code != Code.Add)
						continue;
					if (!DotNetUtils.isStloc(instrs[i + 5]))
						continue;
					return i;
				}
				return -1;
			}

			static int findStartIndex(MethodDefinition method, int endIndex) {
				var instrs = method.Body.Instructions;
				for (int i = endIndex; i >= 0; i--) {
					var instr = instrs[i];
					if (instr.OpCode.FlowControl != FlowControl.Next)
						break;
					if (instr.OpCode.Code == Code.Conv_I8)
						return i;
				}
				return -1;
			}
		}

		class PolyConstantsReader : ConstantsReader {
			long arg;

			public long Arg {
				get { return arg; }
				set { arg = value; }
			}

			public PolyConstantsReader(IList<Instruction> instrs, bool emulateConvInstrs)
				: base(instrs, emulateConvInstrs) {
			}

			protected override bool processInstructionInt64(ref int index, Stack<ConstantInfo<long>> stack) {
				int i = index;

				if (DotNetUtils.isLdloc(instructions[i])) {
					i++;
					if (i >= instructions.Count)
						return false;
				}
				var callvirt = instructions[i];
				if (callvirt.OpCode.Code != Code.Callvirt)
					return false;
				var calledMethod = callvirt.Operand as MethodReference;
				if (calledMethod == null || calledMethod.FullName != "System.Int64 System.IO.BinaryReader::ReadInt64()")
					return false;

				stack.Push(new ConstantInfo<long>(index, arg));
				index = i + 1;
				return true;
			}
		}

		public EmbeddedResource Resource {
			get { return resource; }
		}

		public MethodDefinition Method {
			get { return decryptMethod; }
		}

		public bool Detected {
			get { return decryptMethod != null; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		static string[] requiredLocals = new string[] {
			"System.Byte[]",
			"System.IO.BinaryReader",
			"System.Reflection.Assembly",
		};
		public void find(ISimpleDeobfuscator simpleDeobfuscator) {
			var type = DotNetUtils.getModuleType(module);
			if (type == null)
				return;
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.String", "(System.Int32)"))
					continue;
				var localTypes = new LocalTypes(method);
				if (!localTypes.all(requiredLocals))
					continue;

				simpleDeobfuscator.deobfuscate(method);

				var tmpResource = findResource(method);
				if (tmpResource == null)
					continue;
				if (!findMagic1(method, out magic1))
					continue;
				if (!findMagic2(method, out magic2))
					continue;

				if (!localTypes.exists("System.Random"))
					version = ConfuserVersion.v11_r49299;
				else if (localTypes.exists("System.Collections.Generic.Dictionary`2<System.Int32,System.String>"))
					version = ConfuserVersion.v10_r48832;
				else
					version = ConfuserVersion.v10_r42915;
				resource = tmpResource;
				decryptMethod = method;
				break;
			}
		}

		EmbeddedResource findResource(MethodDefinition method) {
			return DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
		}

		static bool findMagic1(MethodDefinition method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.Byte[] System.IO.BinaryReader::ReadBytes(System.Int32)");
				if (index < 0)
					break;
				if (index < 4)
					continue;

				index -= 4;
				if (!DotNetUtils.isLdarg(instrs[index]))
					continue;
				if (instrs[index + 1].OpCode.Code != Code.Xor)
					continue;
				var ldci4 = instrs[index + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[index + 3].OpCode.Code != Code.Sub)
					continue;

				magic = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			magic = 0;
			return false;
		}

		static bool findMagic2(MethodDefinition method, out uint magic) {
			var instrs = method.Body.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				int index = ConfuserUtils.findCallMethod(instrs, i, Code.Callvirt, "System.UInt32 System.IO.BinaryReader::ReadUInt32()");
				if (index < 0)
					break;
				if (index + 4 >= instrs.Count)
					continue;

				if (instrs[index + 1].OpCode.Code != Code.Not)
					continue;
				var ldci4 = instrs[index + 2];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (instrs[index + 3].OpCode.Code != Code.Xor)
					continue;
				if (!DotNetUtils.isStloc(instrs[index + 4]))
					continue;

				magic = (uint)DotNetUtils.getLdcI4Value(ldci4);
				return true;
			}
			magic = 0;
			return false;
		}

		public void initialize() {
			if (decryptMethod == null)
				return;
			reader = new BinaryReader(new MemoryStream(DeobUtils.inflate(resource.GetResourceData(), true)));

			switch (version) {
			case ConfuserVersion.v10_r42915:
				decrypter = new Decrypter_v10_r42915(this);
				break;

			case ConfuserVersion.v10_r48832:
				decrypter = new Decrypter_v10_r48832(this);
				break;

			case ConfuserVersion.v11_r49299:
				decrypter = new Decrypter_v11_r49299(this);
				break;

			default:
				throw new ApplicationException("Invalid version");
			}
		}

		public string decrypt(MethodDefinition caller, int magic) {
			return decrypter.decrypt(caller, magic);
		}
	}
}
