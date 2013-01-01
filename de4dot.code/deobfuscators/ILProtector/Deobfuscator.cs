/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.ILProtector {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "ILProtector";
		public const string THE_TYPE = "il";

		public DeobfuscatorInfo()
			: base() {
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				ValidNameRegex = validNameRegex.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		MainType mainType;
		MethodsDecrypter methodsDecrypter;

		internal class Options : OptionsBase {
		}

		public override string Type {
			get { return DeobfuscatorInfo.THE_TYPE; }
		}

		public override string TypeLong {
			get { return DeobfuscatorInfo.THE_NAME; }
		}

		public override string Name {
			get { return obfuscatorName; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		protected override int detectInternal() {
			return mainType.Detected ? 150 : 0;
		}

		protected override void scanForObfuscator() {
			mainType = new MainType(module);
			mainType.find();
			methodsDecrypter = new MethodsDecrypter(module, mainType);
			if (mainType.Detected)
				methodsDecrypter.find();

			if (mainType.Detected && methodsDecrypter.Detected && methodsDecrypter.Version != null)
				obfuscatorName += " " + methodsDecrypter.Version;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			if (mainType.Detected) {
				if (methodsDecrypter.Detected) {
					methodsDecrypter.decrypt();
					addTypesToBeRemoved(methodsDecrypter.DelegateTypes, "Obfuscator method delegate type");
					addResourceToBeRemoved(methodsDecrypter.Resource, "Encrypted methods resource");
					addTypeToBeRemoved(mainType.InvokerDelegate, "Invoker delegate type");
					addFieldToBeRemoved(mainType.InvokerInstanceField, "Invoker delegate instance field");
					foreach (var pm in mainType.ProtectMethods)
						addMethodToBeRemoved(pm, "Obfuscator 'Protect' init method");
					mainType.cleanUp();
				}
				else
					Logger.w("New ILProtector version. Can't decrypt methods (yet)");
			}
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			return new List<int>();
		}
	}
}
