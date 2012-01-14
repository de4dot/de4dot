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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Goliath_NET {
	class ArrayValueInliner : MethodReturnValueInliner {
		MethodDefinitionAndDeclaringTypeDict<Func<MethodDefinition, object[], byte[]>> intDecrypters = new MethodDefinitionAndDeclaringTypeDict<Func<MethodDefinition, object[], byte[]>>();
		InitializedDataCreator initializedDataCreator;
		ModuleDefinition module;

		class MyCallResult : CallResult {
			public MethodReference methodReference;
			public MyCallResult(Block block, int callEndIndex, MethodReference method)
				: base(block, callEndIndex) {
				this.methodReference = method;
			}
		}

		public bool HasHandlers {
			get { return intDecrypters.Count != 0; }
		}

		public ArrayValueInliner(ModuleDefinition module, InitializedDataCreator initializedDataCreator) {
			this.module = module;
			this.initializedDataCreator = initializedDataCreator;
		}

		public void add(MethodDefinition method, Func<MethodDefinition, object[], byte[]> handler) {
			if (method == null)
				return;
			if (intDecrypters.find(method) != null)
				throw new ApplicationException(string.Format("Handler for method {0:X8} has already been added", method.MetadataToken.ToInt32()));
			intDecrypters.add(method, handler);
		}

		protected override void inlineReturnValues(IList<CallResult> callResults) {
			foreach (var callResult in callResults) {
				var block = callResult.block;
				int num = callResult.callEndIndex - callResult.callStartIndex + 1;

				var arrayData = (byte[])callResult.returnValue;
				initializedDataCreator.addInitializeArrayCode(block, callResult.callStartIndex, num, module.TypeSystem.Byte, arrayData);
				Log.v("Decrypted array: {0} bytes", arrayData.Length);
			}
		}

		protected override void inlineAllCalls() {
			foreach (var tmp in callResults) {
				var callResult = (MyCallResult)tmp;
				var handler = intDecrypters.find(callResult.methodReference);
				callResult.returnValue = handler((MethodDefinition)callResult.methodReference, callResult.args);
			}
		}

		protected override CallResult createCallResult(MethodReference method, Block block, int callInstrIndex) {
			if (intDecrypters.find(method) == null)
				return null;
			return new MyCallResult(block, callInstrIndex, method);
		}
	}
}
