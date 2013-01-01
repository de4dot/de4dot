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

using System.Collections.Generic;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Goliath_NET {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Goliath.NET";
		public const string THE_TYPE = "go";
		const string DEFAULT_REGEX = @"!^[A-Za-z]{1,2}(?:`\d+)?$&" + DeobfuscatorBase.DEFAULT_VALID_NAME_REGEX;
		BoolOption inlineMethods;
		BoolOption removeInlinedMethods;
		BoolOption restoreLocals;
		BoolOption decryptIntegers;
		BoolOption decryptArrays;
		BoolOption removeAntiStrongName;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			inlineMethods = new BoolOption(null, makeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, makeArgName("remove-inlined"), "Remove inlined methods", true);
			restoreLocals = new BoolOption(null, makeArgName("locals"), "Restore locals", true);
			decryptIntegers = new BoolOption(null, makeArgName("ints"), "Decrypt integers", true);
			decryptArrays = new BoolOption(null, makeArgName("arrays"), "Decrypt arrays", true);
			removeAntiStrongName = new BoolOption(null, makeArgName("sn"), "Remove anti strong name code", true);
		}

		public override string Name {
			get { return THE_NAME; }
		}

		public override string Type {
			get { return THE_TYPE; }
		}

		public override IDeobfuscator createDeobfuscator() {
			return new Deobfuscator(new Deobfuscator.Options {
				RenameResourcesInCode = false,
				ValidNameRegex = validNameRegex.get(),
				InlineMethods = inlineMethods.get(),
				RemoveInlinedMethods = removeInlinedMethods.get(),
				RestoreLocals = restoreLocals.get(),
				DecryptIntegers = decryptIntegers.get(),
				DecryptArrays = decryptArrays.get(),
				RemoveAntiStrongName = removeAntiStrongName.get(),
			});
		}

		protected override IEnumerable<Option> getOptionsInternal() {
			return new List<Option>() {
				inlineMethods,
				removeInlinedMethods,
				restoreLocals,
				decryptIntegers,
				decryptArrays,
				removeAntiStrongName,
			};
		}
	}

	class Deobfuscator : DeobfuscatorBase {
		Options options;
		string obfuscatorName = DeobfuscatorInfo.THE_NAME;

		ProxyCallFixer proxyCallFixer;
		LocalsRestorer localsRestorer;
		LogicalExpressionFixer logicalExpressionFixer;
		StringDecrypter stringDecrypter;
		IntegerDecrypter integerDecrypter;
		Int32ValueInliner int32ValueInliner;
		ArrayDecrypter arrayDecrypter;
		ArrayValueInliner arrayValueInliner;
		StrongNameChecker strongNameChecker;

		bool foundGoliathAttribute = false;
		bool startedDeobfuscating = false;

		internal class Options : OptionsBase {
			public bool InlineMethods { get; set; }
			public bool RemoveInlinedMethods { get; set; }
			public bool RestoreLocals { get; set; }
			public bool DecryptIntegers { get; set; }
			public bool DecryptArrays { get; set; }
			public bool RemoveAntiStrongName { get; set; }
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

		protected override bool CanInlineMethods {
			get { return startedDeobfuscating ? options.InlineMethods : true; }
		}

		internal Deobfuscator(Options options)
			: base(options) {
			this.options = options;
		}

		protected override int detectInternal() {
			int val = 0;

			int sum = toInt32(stringDecrypter.Detected) +
					toInt32(integerDecrypter.Detected) +
					toInt32(arrayDecrypter.Detected) +
					toInt32(strongNameChecker.Detected) +
					toInt32(hasMetadataStream("#GOLIATH"));
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (foundGoliathAttribute)
				val += 10;

			return val;
		}

		protected override void scanForObfuscator() {
			findGoliathAttribute();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.find();
			integerDecrypter = new IntegerDecrypter(module);
			integerDecrypter.find();
			arrayDecrypter = new ArrayDecrypter(module);
			arrayDecrypter.find();
			strongNameChecker = new StrongNameChecker(module);
			strongNameChecker.find();
		}

		void findGoliathAttribute() {
			foreach (var type in module.Types) {
				if (type.FullName.Contains("ObfuscatedByGoliath")) {
					foundGoliathAttribute = true;
					addAttributeToBeRemoved(type, "Obfuscator attribute");
					initializeVersion(type);
					break;
				}
			}
		}

		void initializeVersion(TypeDef attr) {
			var s = DotNetUtils.getCustomArgAsString(getAssemblyAttribute(attr), 0);
			if (s == null)
				return;

			var val = System.Text.RegularExpressions.Regex.Match(s, @"^Goliath \.NET Obfuscator rel\. (\d+\.\d+\.\d+)$");
			if (val.Groups.Count < 2)
				return;
			obfuscatorName = DeobfuscatorInfo.THE_NAME + " " + val.Groups[1].ToString();
			return;
		}

		public override void deobfuscateBegin() {
			base.deobfuscateBegin();

			proxyCallFixer = new ProxyCallFixer(module);
			proxyCallFixer.find();
			localsRestorer = new LocalsRestorer(module);
			if (options.RestoreLocals)
				localsRestorer.find();

			logicalExpressionFixer = new LogicalExpressionFixer();
			stringDecrypter.initialize();
			integerDecrypter.initialize();
			arrayDecrypter.initialize();

			if (options.DecryptIntegers) {
				int32ValueInliner = new Int32ValueInliner();
				foreach (var method in integerDecrypter.getMethods()) {
					int32ValueInliner.add(method, (method2, gim, args) => {
						return integerDecrypter.decrypt(method2);
					});
				}
			}

			if (options.DecryptArrays) {
				arrayValueInliner = new ArrayValueInliner(module, initializedDataCreator);
				foreach (var method in arrayDecrypter.getMethods()) {
					arrayValueInliner.add(method, (method2, gim, args) => {
						return arrayDecrypter.decrypt(method2);
					});
				}
			}

			foreach (var method in stringDecrypter.getMethods()) {
				staticStringInliner.add(method, (method2, gim, args) => {
					return stringDecrypter.decrypt(method2);
				});
				DeobfuscatedFile.stringDecryptersAdded();
			}

			if (options.RemoveAntiStrongName)
				addTypeToBeRemoved(strongNameChecker.Type, "Strong name checker type");

			startedDeobfuscating = true;
		}

		public override void deobfuscateMethodBegin(Blocks blocks) {
			proxyCallFixer.deobfuscate(blocks);
			base.deobfuscateMethodBegin(blocks);
		}

		public override void deobfuscateMethodEnd(Blocks blocks) {
			stringDecrypter.deobfuscate(blocks);
			int32ValueInliner.decrypt(blocks);
			arrayValueInliner.decrypt(blocks);
			if (options.RestoreLocals)
				localsRestorer.deobfuscate(blocks);
			if (options.RemoveAntiStrongName) {
				if (strongNameChecker.deobfuscate(blocks))
					Logger.v("Removed strong name checker code");
			}
			logicalExpressionFixer.deobfuscate(blocks);
			base.deobfuscateMethodEnd(blocks);
		}

		public override void deobfuscateEnd() {
			removeProxyDelegates(proxyCallFixer);
			removeInlinedMethods();
			addTypesToBeRemoved(localsRestorer.Types, "Method locals obfuscation type");

			if (CanRemoveStringDecrypterType) {
				removeDecrypterStuff(stringDecrypter, "String", "strings");
				addTypeToBeRemoved(stringDecrypter.StringStruct, "String struct");
			}
			if (options.DecryptIntegers)
				removeDecrypterStuff(integerDecrypter, "Integer", "integers");
			if (options.DecryptArrays)
				removeDecrypterStuff(arrayDecrypter, "Array", "arrays");

			base.deobfuscateEnd();
		}

		void removeDecrypterStuff(DecrypterBase decrypter, string name1, string name2) {
			addResourceToBeRemoved(decrypter.EncryptedResource, "Encrypted " + name2);
			addTypesToBeRemoved(decrypter.DecrypterTypes, name1 + " decrypter type");
			addTypeToBeRemoved(decrypter.Type, name1 + " resource decrypter type");
			if (decrypter.DelegateInitType != null) {
				addTypeToBeRemoved(decrypter.DelegateType, name1 + " resource decrypter delegate type");
				addTypeToBeRemoved(decrypter.DelegateInitType, name1 + " delegate initializer type");
			}
		}

		void removeInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;
			findAndRemoveInlinedMethods();
		}

		public override IEnumerable<int> getStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var method in stringDecrypter.getMethods())
				list.Add(method.MDToken.ToInt32());
			return list;
		}
	}
}
