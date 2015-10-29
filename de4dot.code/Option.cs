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
using System.Text.RegularExpressions;

namespace de4dot.code {
	public abstract class Option {
		const string SHORTNAME_PREFIX = "-";
		const string LONGNAME_PREFIX = "--";

		string shortName;
		string longName;
		string description;
		object defaultVal;

		public string ShortName {
			get { return shortName; }
		}

		public string LongName {
			get { return longName; }
		}

		public string Description {
			get { return description; }
		}

		public object Default {
			get { return defaultVal; }
			protected set { defaultVal = value; }
		}

		public virtual bool NeedArgument {
			get { return true; }
		}

		public virtual string ArgumentValueName {
			get { return "value"; }
		}

		// Returns true if the new value is set, or false on error. error string is also updated.
		public abstract bool Set(string val, out string error);

		public Option(string shortName, string longName, string description) {
			if (shortName != null)
				this.shortName = SHORTNAME_PREFIX + shortName;
			if (longName != null)
				this.longName = LONGNAME_PREFIX + longName;
			this.description = description;
		}
	}

	public class BoolOption : Option {
		bool val;
		public BoolOption(string shortName, string longName, string description, bool val)
			: base(shortName, longName, description) {
			Default = this.val = val;
		}

		public override string ArgumentValueName {
			get { return "bool"; }
		}

		public override bool Set(string newVal, out string error) {
			if (string.Equals(newVal, "false", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(newVal, "off", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(newVal, "0", StringComparison.OrdinalIgnoreCase)) {
				val = false;
			}
			else
				val = true;
			error = "";
			return true;
		}

		public bool Get() {
			return val;
		}
	}

	public class IntOption : Option {
		int val;
		public IntOption(string shortName, string longName, string description, int val)
			: base(shortName, longName, description) {
			Default = this.val = val;
		}

		public override string ArgumentValueName {
			get { return "int"; }
		}

		public override bool Set(string newVal, out string error) {
			int newInt;
			if (!int.TryParse(newVal, out newInt)) {
				error = string.Format("Not an integer: '{0}'", newVal);
				return false;
			}
			val = newInt;
			error = "";
			return true;
		}

		public int Get() {
			return val;
		}
	}

	public class StringOption : Option {
		string val;

		public override string ArgumentValueName {
			get { return "string"; }
		}

		public StringOption(string shortName, string longName, string description, string val)
			: base(shortName, longName, description) {
			Default = this.val = val;
		}

		public override bool Set(string newVal, out string error) {
			val = newVal;
			error = "";
			return true;
		}

		public string Get() {
			return val;
		}
	}

	public class NameRegexOption : Option {
		NameRegexes val;

		public override string ArgumentValueName {
			get { return "regex"; }
		}

		public NameRegexOption(string shortName, string longName, string description, string val)
			: base(shortName, longName, description) {
			Default = this.val = new NameRegexes(val);
		}

		public override bool Set(string newVal, out string error) {
			try {
				var regexes = new NameRegexes();
				regexes.Set(newVal);
				val = regexes;
			}
			catch (ArgumentException) {
				error = string.Format("Could not parse regex '{0}'", newVal);
				return false;
			}
			error = "";
			return true;
		}

		public NameRegexes Get() {
			return val;
		}
	}

	public class RegexOption : Option {
		Regex val;

		public override string ArgumentValueName {
			get { return "regex"; }
		}

		public RegexOption(string shortName, string longName, string description, string val)
			: base(shortName, longName, description) {
			Default = this.val = new Regex(val);
		}

		public override bool Set(string newVal, out string error) {
			try {
				val = new Regex(newVal);
			}
			catch (ArgumentException) {
				error = string.Format("Could not parse regex '{0}'", newVal);
				return false;
			}
			error = "";
			return true;
		}

		public Regex Get() {
			return val;
		}
	}

	public class NoArgOption : Option {
		Action action;
		bool triggered;

		public override bool NeedArgument {
			get { return false; }
		}

		public NoArgOption(string shortName, string longName, string description)
			: this(shortName, longName, description, null) {
		}

		public NoArgOption(string shortName, string longName, string description, Action action)
			: base(shortName, longName, description) {
			this.action = action;
		}

		public override bool Set(string val, out string error) {
			triggered = true;
			if (action != null)
				action();
			error = "";
			return true;
		}

		public bool Get() {
			return triggered;
		}
	}

	public class OneArgOption : Option {
		Action<string> action;
		string typeName;

		public override string ArgumentValueName {
			get { return typeName; }
		}

		public OneArgOption(string shortName, string longName, string description, string typeName, Action<string> action)
			: base(shortName, longName, description) {
			this.typeName = typeName ?? "value";
			this.action = action;
			Default = null;
		}

		public override bool Set(string val, out string error) {
			action(val);
			error = "";
			return true;
		}
	}
}
