/*
    Copyright (C) 2011 de4dot@gmail.com

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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.AssemblyClient;
using de4dot.blocks;

namespace de4dot {
	// A simple class that statically detects the values of some local variables
	class VariableValues {
		IList<Block> allBlocks;
		IList<VariableDefinition> locals;
		Dictionary<VariableDefinition, Variable> variableToValue = new Dictionary<VariableDefinition, Variable>();

		public class Variable {
			int writes = 0;
			object value;
			bool unknownValue = false;

			public bool isValid() {
				return !unknownValue && writes == 1;
			}

			public object Value {
				get {
					if (!isValid())
						throw new ApplicationException("Unknown variable value");
					return value;
				}
				set { this.value = value; }
			}

			public void addWrite() {
				writes++;
			}

			public void setUnknown() {
				unknownValue = true;
			}
		}

		public VariableValues(IList<VariableDefinition> locals, IList<Block> allBlocks) {
			this.locals = locals;
			this.allBlocks = allBlocks;
			init();
		}

		void init() {
			foreach (var variable in locals)
				variableToValue[variable] = new Variable();

			foreach (var block in allBlocks) {
				for (int i = 0; i < block.Instructions.Count; i++) {
					var instr = block.Instructions[i];

					switch (instr.OpCode.Code) {
					case Code.Stloc:
					case Code.Stloc_S:
					case Code.Stloc_0:
					case Code.Stloc_1:
					case Code.Stloc_2:
					case Code.Stloc_3:
						var variable = Instr.getLocalVar(locals, instr);
						var val = variableToValue[variable];
						val.addWrite();
						object obj;
						if (!getValue(block, i, out obj))
							val.setUnknown();
						val.Value = obj;
						break;

					default:
						break;
					}
				}
			}
		}

		bool getValue(Block block, int index, out object obj) {
			while (true) {
				if (index <= 0) {
					obj = null;
					return false;
				}
				var instr = block.Instructions[--index];
				if (instr.OpCode == OpCodes.Nop)
					continue;

				switch (instr.OpCode.Code) {
				case Code.Ldc_I4:
				case Code.Ldc_I8:
				case Code.Ldc_R4:
				case Code.Ldc_R8:
				case Code.Ldstr:
					obj = instr.Operand;
					return true;
				case Code.Ldc_I4_S:
					obj = (int)(sbyte)instr.Operand;
					return true;

				case Code.Ldc_I4_0: obj = 0; return true;
				case Code.Ldc_I4_1: obj = 1; return true;
				case Code.Ldc_I4_2: obj = 2; return true;
				case Code.Ldc_I4_3: obj = 3; return true;
				case Code.Ldc_I4_4: obj = 4; return true;
				case Code.Ldc_I4_5: obj = 5; return true;
				case Code.Ldc_I4_6: obj = 6; return true;
				case Code.Ldc_I4_7: obj = 7; return true;
				case Code.Ldc_I4_8: obj = 8; return true;
				case Code.Ldc_I4_M1:obj = -1; return true;
				case Code.Ldnull:	obj = null; return true;

				default:
					obj = null;
					return false;
				}
			}
		}

		public Variable getValue(VariableDefinition variable) {
			return variableToValue[variable];
		}
	}

	abstract class StringDecrypterBase {
		protected List<DecryptCall> decryptCalls;
		List<Block> allBlocks;
		Blocks blocks;
		VariableValues variableValues;

		protected class DecryptCall {
			public Block block;
			public int callStartIndex;
			public int callEndIndex;
			public object[] args;
			public string decryptedString;

			public DecryptCall(Block block, int callEndIndex) {
				this.block = block;
				this.callEndIndex = callEndIndex;
			}

			public MethodReference getMethodReference() {
				return (MethodReference)block.Instructions[callEndIndex].Operand;
			}
		}

		protected abstract void decryptAllCalls();

		// Returns null if method is not a string decrypter
		protected abstract DecryptCall createDecryptCall(MethodReference method, Block block, int callInstrIndex);

		public void decrypt(Blocks theBlocks) {
			try {
				blocks = theBlocks;
				decryptCalls = new List<DecryptCall>();
				allBlocks = new List<Block>(blocks.MethodBlocks.getAllBlocks());

				findAllDecryptCalls();
				decryptAllCalls();
				restoreDecryptedStrings();
			}
			finally {
				blocks = null;
				decryptCalls = null;
				allBlocks = null;
				variableValues = null;
			}
		}

		void getLocalVariableValue(VariableDefinition variable, out object value) {
			if (variableValues == null)
				variableValues = new VariableValues(blocks.Locals, allBlocks);
			var val = variableValues.getValue(variable);
			if (!val.isValid())
				throw new ApplicationException("Could not get value of local variable");
			value = val.Value;
		}

		void findAllDecryptCalls() {
			foreach (var block in allBlocks)
				findDecryptCalls(block);
		}

		void findDecryptCalls(Block block) {
			for (int i = 0; i < block.Instructions.Count; i++) {
				var instr = block.Instructions[i];
				if (instr.OpCode != OpCodes.Call)
					continue;
				var method = instr.Operand as MethodReference;
				if (method == null)
					continue;

				var decryptCall = createDecryptCall(method, block, i);
				if (decryptCall == null)
					continue;

				decryptCalls.Add(decryptCall);
				findArgs(decryptCall);
			}
		}

		void findArgs(DecryptCall decryptCall) {
			var block = decryptCall.block;
			var method = decryptCall.getMethodReference();
			int numArgs = method.Parameters.Count + (method.HasThis ? 1 : 0);
			var args = new object[numArgs];

			int instrIndex = decryptCall.callEndIndex - 1;
			for (int i = numArgs - 1; i >= 0; i--)
				getArg(method, block, ref args[i], ref instrIndex);

			decryptCall.args = args;
			decryptCall.callStartIndex = instrIndex + 1;
		}

		void getArg(MethodReference method, Block block, ref object arg, ref int instrIndex) {
			while (true) {
				if (instrIndex < 0)
					throw new ApplicationException(string.Format("Could not find all arguments to method {0}", method));

				var instr = block.Instructions[instrIndex--];
				switch (instr.OpCode.Code) {
				case Code.Ldc_I4:
				case Code.Ldc_I8:
				case Code.Ldc_R4:
				case Code.Ldc_R8:
				case Code.Ldstr:
					arg = instr.Operand;
					break;
				case Code.Ldc_I4_S:
					arg = (int)(sbyte)instr.Operand;
					break;

				case Code.Ldc_I4_0: arg = 0; break;
				case Code.Ldc_I4_1: arg = 1; break;
				case Code.Ldc_I4_2: arg = 2; break;
				case Code.Ldc_I4_3: arg = 3; break;
				case Code.Ldc_I4_4: arg = 4; break;
				case Code.Ldc_I4_5: arg = 5; break;
				case Code.Ldc_I4_6: arg = 6; break;
				case Code.Ldc_I4_7: arg = 7; break;
				case Code.Ldc_I4_8: arg = 8; break;
				case Code.Ldc_I4_M1:arg = -1; break;
				case Code.Ldnull:	arg = null; break;

				case Code.Nop:
					continue;

				case Code.Ldloc:
				case Code.Ldloc_S:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
					getLocalVariableValue(Instr.getLocalVar(blocks.Locals, instr), out arg);
					break;

				case Code.Ldsfld:
					arg = instr.Operand;
					break;

				default:
					throw new ApplicationException(string.Format("Could not find all arguments to method {0}, instr: {1}", method, instr));
				}
				break;
			}
		}

		void restoreDecryptedStrings() {
			decryptCalls.Sort((a, b) => {
				int i1 = allBlocks.FindIndex((x) => a.block == x);
				int i2 = allBlocks.FindIndex((x) => b.block == x);
				if (i1 < i2) return -1;
				if (i1 > i2) return 1;

				if (a.callStartIndex < b.callStartIndex) return -1;
				if (a.callStartIndex > b.callStartIndex) return 1;

				return 0;
			});
			decryptCalls.Reverse();

			foreach (var decryptCall in decryptCalls) {
				var block = decryptCall.block;
				int num = decryptCall.callEndIndex - decryptCall.callStartIndex + 1;
				block.replace(decryptCall.callStartIndex, num, Instruction.Create(OpCodes.Ldstr, decryptCall.decryptedString));
				Log.v("Decrypted string: {0}", Utils.toCsharpString(decryptCall.decryptedString));
			}
		}
	}

	class DynamicStringDecrypter : StringDecrypterBase {
		IAssemblyClient assemblyClient;
		Dictionary<int, int> methodTokenToId = new Dictionary<int, int>();

		class MyDecryptCall : DecryptCall {
			public int methodId;
			public MyDecryptCall(Block block, int callEndIndex, int methodId)
				: base(block, callEndIndex) {
				this.methodId = methodId;
			}
		}

		public DynamicStringDecrypter(IAssemblyClient assemblyClient) {
			this.assemblyClient = assemblyClient;
		}

		public void init(IEnumerable<int> methodTokens) {
			methodTokenToId.Clear();
			foreach (var methodToken in methodTokens) {
				if (methodTokenToId.ContainsKey(methodToken))
					continue;
				methodTokenToId[methodToken] = assemblyClient.Service.defineStringDecrypter(methodToken);
			}
		}

		protected override DecryptCall createDecryptCall(MethodReference method, Block block, int callInstrIndex) {
			int methodId;
			if (!methodTokenToId.TryGetValue(method.MetadataToken.ToInt32(), out methodId))
				return null;
			return new MyDecryptCall(block, callInstrIndex, methodId);
		}

		protected override void decryptAllCalls() {
			var sortedCalls = new Dictionary<int, List<MyDecryptCall>>();
			foreach (var tmp in decryptCalls) {
				var decryptCall = (MyDecryptCall)tmp;
				List<MyDecryptCall> list;
				if (!sortedCalls.TryGetValue(decryptCall.methodId, out list))
					sortedCalls[decryptCall.methodId] = list = new List<MyDecryptCall>(decryptCalls.Count);
				list.Add(decryptCall);
			}

			foreach (var methodId in sortedCalls.Keys) {
				var list = sortedCalls[methodId];
				var args = new object[list.Count];
				for (int i = 0; i < list.Count; i++) {
					AssemblyData.SimpleData.pack(list[i].args);
					args[i] = list[i].args;
				}
				var decryptedStrings = assemblyClient.Service.decryptStrings(methodId, args);
				if (decryptedStrings.Length != args.Length)
					throw new ApplicationException("Invalid decrypted strings array length");
				AssemblyData.SimpleData.unpack(decryptedStrings);
				for (int i = 0; i < list.Count; i++) {
					var s = decryptedStrings[i];
					if (s == null)
						throw new ApplicationException(string.Format("Decrypted string is null. Method: {0}", list[i].getMethodReference()));
					list[i].decryptedString = (string)s;
				}
			}
		}
	}

	class StaticStringDecrypter : StringDecrypterBase {
		Dictionary<MethodReferenceAndDeclaringTypeKey, Func<MethodDefinition, object[], string>> stringDecrypters = new Dictionary<MethodReferenceAndDeclaringTypeKey, Func<MethodDefinition, object[], string>>();

		public bool HasHandlers {
			get { return stringDecrypters.Count != 0; }
		}

		public IEnumerable<MethodDefinition> Methods {
			get {
				var list = new List<MethodDefinition>(stringDecrypters.Count);
				foreach (var key in stringDecrypters.Keys)
					list.Add((MethodDefinition)key.MethodReference);
				return list;
			}
		}

		class MyDecryptCall : DecryptCall {
			public MethodReferenceAndDeclaringTypeKey methodKey;
			public MyDecryptCall(Block block, int callEndIndex, MethodReference method)
				: base(block, callEndIndex) {
				this.methodKey = new MethodReferenceAndDeclaringTypeKey(method);
			}
		}

		public void add(MethodDefinition method, Func<MethodReference, object[], string> handler) {
			if (method != null)
				stringDecrypters[new MethodReferenceAndDeclaringTypeKey(method)] = handler;
		}

		protected override void decryptAllCalls() {
			foreach (var tmp in decryptCalls) {
				var decryptCall = (MyDecryptCall)tmp;
				var handler = stringDecrypters[decryptCall.methodKey];
				decryptCall.decryptedString = handler((MethodDefinition)decryptCall.methodKey.MethodReference, decryptCall.args);
			}
		}

		protected override DecryptCall createDecryptCall(MethodReference method, Block block, int callInstrIndex) {
			if (!stringDecrypters.ContainsKey(new MethodReferenceAndDeclaringTypeKey(method)))
				return null;
			return new MyDecryptCall(block, callInstrIndex, method);
		}
	}
}
