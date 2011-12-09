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
using de4dot.code.AssemblyClient;
using de4dot.blocks;

namespace de4dot.code {
	abstract class StringDecrypter : MethodReturnValueInliner {
		protected override void inlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				int ldstrIndex = callResult.callStartIndex;
				block.replace(ldstrIndex, num, Instruction.Create(OpCodes.Ldstr, (string)callResult.returnValue));

				// If it's followed by String.Intern(), then nop out that call
				if (ldstrIndex + 1 < block.Instructions.Count) {
					var instr = block.Instructions[ldstrIndex + 1];
					if (instr.OpCode.Code == Code.Call) {
						var calledMethod = instr.Operand as MethodReference;
						if (calledMethod != null &&
							calledMethod.FullName == "System.String System.String::Intern(System.String)") {
							block.remove(ldstrIndex + 1, 1);
						}
					}
				}

				Log.v("Decrypted string: {0}", Utils.toCsharpString((string)callResult.returnValue));
			}
		}
	}

	class DynamicStringDecrypter : StringDecrypter {
		IAssemblyClient assemblyClient;
		Dictionary<int, int> methodTokenToId = new Dictionary<int, int>();

		class MyCallResult : CallResult {
			public int methodId;
			public MyCallResult(Block block, int callEndIndex, int methodId)
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

		protected override CallResult createCallResult(MethodReference method, Block block, int callInstrIndex) {
			int methodId;
			if (!methodTokenToId.TryGetValue(method.MetadataToken.ToInt32(), out methodId))
				return null;
			return new MyCallResult(block, callInstrIndex, methodId);
		}

		protected override void inlineAllCalls() {
			var sortedCalls = new Dictionary<int, List<MyCallResult>>();
			foreach (var tmp in callResults) {
				var callResult = (MyCallResult)tmp;
				List<MyCallResult> list;
				if (!sortedCalls.TryGetValue(callResult.methodId, out list))
					sortedCalls[callResult.methodId] = list = new List<MyCallResult>(callResults.Count);
				list.Add(callResult);
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
					list[i].returnValue = (string)s;
				}
			}
		}
	}

	class StaticStringDecrypter : StringDecrypter {
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

		class MyCallResult : CallResult {
			public MethodReferenceAndDeclaringTypeKey methodKey;
			public MyCallResult(Block block, int callEndIndex, MethodReference method)
				: base(block, callEndIndex) {
				this.methodKey = new MethodReferenceAndDeclaringTypeKey(method);
			}
		}

		public void add(MethodDefinition method, Func<MethodDefinition, object[], string> handler) {
			if (method != null)
				stringDecrypters[new MethodReferenceAndDeclaringTypeKey(method)] = handler;
		}

		protected override void inlineAllCalls() {
			foreach (var tmp in callResults) {
				var callResult = (MyCallResult)tmp;
				var handler = stringDecrypters[callResult.methodKey];
				callResult.returnValue = handler((MethodDefinition)callResult.methodKey.MethodReference, callResult.args);
			}
		}

		protected override CallResult createCallResult(MethodReference method, Block block, int callInstrIndex) {
			if (!stringDecrypters.ContainsKey(new MethodReferenceAndDeclaringTypeKey(method)))
				return null;
			return new MyCallResult(block, callInstrIndex, method);
		}
	}
}
