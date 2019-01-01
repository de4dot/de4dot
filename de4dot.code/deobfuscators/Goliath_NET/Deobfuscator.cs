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

using System.Collections.Generic;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Goliath_NET {
	public class DeobfuscatorInfo : DeobfuscatorInfoBase {
		public const string THE_NAME = "Goliath.NET";
		public const string THE_TYPE = "go";
		const string DEFAULT_REGEX = @"!^[A-Za-z]{1,2}(?:`\d+)?$&" + DeobfuscatorBase.DEFAULT_ASIAN_VALID_NAME_REGEX;
		BoolOption inlineMethods;
		BoolOption removeInlinedMethods;
		BoolOption restoreLocals;
		BoolOption decryptIntegers;
		BoolOption decryptArrays;
		BoolOption removeAntiStrongName;

		public DeobfuscatorInfo()
			: base(DEFAULT_REGEX) {
			inlineMethods = new BoolOption(null, MakeArgName("inline"), "Inline short methods", true);
			removeInlinedMethods = new BoolOption(null, MakeArgName("remove-inlined"), "Remove inlined methods", true);
			restoreLocals = new BoolOption(null, MakeArgName("locals"), "Restore locals", true);
			decryptIntegers = new BoolOption(null, MakeArgName("ints"), "Decrypt integers", true);
			decryptArrays = new BoolOption(null, MakeArgName("arrays"), "Decrypt arrays", true);
			removeAntiStrongName = new BoolOption(null, MakeArgName("sn"), "Remove anti strong name code", true);
		}

		public override string Name => THE_NAME;
		public override string Type => THE_TYPE;

		public override IDeobfuscator CreateDeobfuscator() =>
			new Deobfuscator(new Deobfuscator.Options {
				RenameResourcesInCode = false,
				ValidNameRegex = validNameRegex.Get(),
				InlineMethods = inlineMethods.Get(),
				RemoveInlinedMethods = removeInlinedMethods.Get(),
				RestoreLocals = restoreLocals.Get(),
				DecryptIntegers = decryptIntegers.Get(),
				DecryptArrays = decryptArrays.Get(),
				RemoveAntiStrongName = removeAntiStrongName.Get(),
			});

		protected override IEnumerable<Option> GetOptionsInternal() =>
			new List<Option>() {
				inlineMethods,
				removeInlinedMethods,
				restoreLocals,
				decryptIntegers,
				decryptArrays,
				removeAntiStrongName,
			};
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

		public override string Type => DeobfuscatorInfo.THE_TYPE;
		public override string TypeLong => DeobfuscatorInfo.THE_NAME;
		public override string Name => obfuscatorName;
		protected override bool CanInlineMethods => startedDeobfuscating ? options.InlineMethods : true;
		internal Deobfuscator(Options options) : base(options) => this.options = options;

		protected override int DetectInternal() {
			int val = 0;

			int sum = ToInt32(stringDecrypter.Detected) +
					ToInt32(integerDecrypter.Detected) +
					ToInt32(arrayDecrypter.Detected) +
					ToInt32(strongNameChecker.Detected) +
					ToInt32(HasMetadataStream("#GOLIATH"));
			if (sum > 0)
				val += 100 + 10 * (sum - 1);
			if (foundGoliathAttribute)
				val += 10;

			return val;
		}

		protected override void ScanForObfuscator() {
			FindGoliathAttribute();
			stringDecrypter = new StringDecrypter(module);
			stringDecrypter.Find();
			integerDecrypter = new IntegerDecrypter(module);
			integerDecrypter.Find();
			arrayDecrypter = new ArrayDecrypter(module);
			arrayDecrypter.Find();
			strongNameChecker = new StrongNameChecker(module);
			strongNameChecker.Find();
		}

		void FindGoliathAttribute() {
			foreach (var type in module.Types) {
				if (type.FullName.Contains("ObfuscatedByGoliath")) {
					foundGoliathAttribute = true;
					AddAttributeToBeRemoved(type, "Obfuscator attribute");
					InitializeVersion(type);
					break;
				}
			}
		}

		void InitializeVersion(TypeDef attr) {
			var s = DotNetUtils.GetCustomArgAsString(GetAssemblyAttribute(attr), 0);
			if (s == null)
				return;

			var val = System.Text.RegularExpressions.Regex.Match(s, @"^Goliath \.NET Obfuscator rel\. (\d+\.\d+\.\d+)$");
			if (val.Groups.Count < 2)
				return;
			obfuscatorName = DeobfuscatorInfo.THE_NAME + " " + val.Groups[1].ToString();
			return;
		}

		public override void DeobfuscateBegin() {
			base.DeobfuscateBegin();

			proxyCallFixer = new ProxyCallFixer(module);
			proxyCallFixer.Find();
			localsRestorer = new LocalsRestorer(module);
			if (options.RestoreLocals)
				localsRestorer.Find();

			logicalExpressionFixer = new LogicalExpressionFixer();
			stringDecrypter.Initialize();
			integerDecrypter.Initialize();
			arrayDecrypter.Initialize();

			if (options.DecryptIntegers) {
				int32ValueInliner = new Int32ValueInliner();
				foreach (var method in integerDecrypter.GetMethods()) {
					int32ValueInliner.Add(method, (method2, gim, args) => {
						return integerDecrypter.Decrypt(method2);
					});
				}
			}

			if (options.DecryptArrays) {
				arrayValueInliner = new ArrayValueInliner(module, initializedDataCreator);
				foreach (var method in arrayDecrypter.GetMethods()) {
					arrayValueInliner.Add(method, (method2, gim, args) => {
						return arrayDecrypter.Decrypt(method2);
					});
				}
			}

			foreach (var method in stringDecrypter.GetMethods()) {
				staticStringInliner.Add(method, (method2, gim, args) => {
					return stringDecrypter.Decrypt(method2);
				});
				DeobfuscatedFile.StringDecryptersAdded();
			}

			if (options.RemoveAntiStrongName)
				AddTypeToBeRemoved(strongNameChecker.Type, "Strong name checker type");

			startedDeobfuscating = true;
		}

		public override void DeobfuscateMethodBegin(Blocks blocks) {
			proxyCallFixer.Deobfuscate(blocks);
			base.DeobfuscateMethodBegin(blocks);
		}

		public override void DeobfuscateMethodEnd(Blocks blocks) {
			stringDecrypter.Deobfuscate(blocks);
			int32ValueInliner.Decrypt(blocks);
			arrayValueInliner.Decrypt(blocks);
			if (options.RestoreLocals)
				localsRestorer.Deobfuscate(blocks);
			if (options.RemoveAntiStrongName) {
				if (strongNameChecker.Deobfuscate(blocks))
					Logger.v("Removed strong name checker code");
			}
			logicalExpressionFixer.Deobfuscate(blocks);
			base.DeobfuscateMethodEnd(blocks);
		}

		public override void DeobfuscateEnd() {
			RemoveProxyDelegates(proxyCallFixer);
			RemoveInlinedMethods();
			AddTypesToBeRemoved(localsRestorer.Types, "Method locals obfuscation type");

			if (CanRemoveStringDecrypterType) {
				RemoveDecrypterStuff(stringDecrypter, "String", "strings");
				AddTypeToBeRemoved(stringDecrypter.StringStruct, "String struct");
			}
			if (options.DecryptIntegers)
				RemoveDecrypterStuff(integerDecrypter, "Integer", "integers");
			if (options.DecryptArrays)
				RemoveDecrypterStuff(arrayDecrypter, "Array", "arrays");

			base.DeobfuscateEnd();
		}

		void RemoveDecrypterStuff(DecrypterBase decrypter, string name1, string name2) {
			AddResourceToBeRemoved(decrypter.EncryptedResource, "Encrypted " + name2);
			AddTypesToBeRemoved(decrypter.DecrypterTypes, name1 + " decrypter type");
			AddTypeToBeRemoved(decrypter.Type, name1 + " resource decrypter type");
			if (decrypter.DelegateInitType != null) {
				AddTypeToBeRemoved(decrypter.DelegateType, name1 + " resource decrypter delegate type");
				AddTypeToBeRemoved(decrypter.DelegateInitType, name1 + " delegate initializer type");
			}
		}

		void RemoveInlinedMethods() {
			if (!options.InlineMethods || !options.RemoveInlinedMethods)
				return;
			FindAndRemoveInlinedMethods();
		}

		public override IEnumerable<int> GetStringDecrypterMethods() {
			var list = new List<int>();
			foreach (var method in stringDecrypter.GetMethods())
				list.Add(method.MDToken.ToInt32());
			return list;
		}
	}
}
