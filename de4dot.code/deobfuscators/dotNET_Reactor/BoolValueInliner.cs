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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class BoolValueInliner : MethodReturnValueInliner {
		Dictionary<MethodReferenceAndDeclaringTypeKey, Func<MethodDefinition, object[], bool>> boolDecrypters = new Dictionary<MethodReferenceAndDeclaringTypeKey, Func<MethodDefinition, object[], bool>>();

		class MyCallResult : CallResult {
			public MethodReferenceAndDeclaringTypeKey methodKey;
			public MyCallResult(Block block, int callEndIndex, MethodReference method)
				: base(block, callEndIndex) {
				this.methodKey = new MethodReferenceAndDeclaringTypeKey(method);
			}
		}

		public bool HasHandlers {
			get { return boolDecrypters.Count != 0; }
		}

		public void add(MethodDefinition method, Func<MethodDefinition, object[], bool> handler) {
			if (method != null)
				boolDecrypters[new MethodReferenceAndDeclaringTypeKey(method)] = handler;
		}

		protected override void inlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				block.replace(callResult.callStartIndex, num, DotNetUtils.createLdci4((bool)callResult.returnValue ? 1 : 0));
				Log.v("Decrypted boolean: {0}", callResult.returnValue);
			}
		}

		protected override void inlineAllCalls() {
			foreach (var tmp in callResults) {
				var callResult = (MyCallResult)tmp;
				var handler = boolDecrypters[callResult.methodKey];
				callResult.returnValue = handler((MethodDefinition)callResult.methodKey.MethodReference, callResult.args);
			}
		}

		protected override CallResult createCallResult(MethodReference method, Block block, int callInstrIndex) {
			if (!boolDecrypters.ContainsKey(new MethodReferenceAndDeclaringTypeKey(method)))
				return null;
			return new MyCallResult(block, callInstrIndex, method);
		}
	}
}
