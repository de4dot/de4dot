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
using de4dot.blocks;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public DeobfuscatorInfo()
			: base("dr") {
		}

		internal static string ObfuscatorType {
			get { return "DotNetReactor"; }
		}

		public override string Type {
			get { return ObfuscatorType; }
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

		MethodsDecrypter methodsDecrypter;

		internal class Options : OptionsBase {
		}

		public override string Type {
			get { return DeobfuscatorInfo.ObfuscatorType; }
		}

		public override string Name {
			get { return ".NET Reactor"; }
		}

		public Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		public override void init(ModuleDefinition module) {
			base.init(module);
		}

		public override int detect() {
			scanForObfuscator();

			int val = 0;

			if (methodsDecrypter.Detected)
				val = 100;

			return val;
		}

		protected override void scanForObfuscatorInternal() {
			methodsDecrypter = new MethodsDecrypter(module);
			methodsDecrypter.find();
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			base.deobfuscateEnd();
		}

		public override IEnumerable<string> getStringDecrypterMethods() {
			var list = new List<string>();
			return list;
		}
	}
}
