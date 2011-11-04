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

using Mono.Cecil;

namespace de4dot.renamer {
	// State when renaming type members
	class VariableNameState {
		CurrentNames currentVariableNames = new CurrentNames();
		CurrentNames currentMethodNames = new CurrentNames();
		protected TypeNames variableNameCreator = new VariableNameCreator();	// For fields and method args
		protected TypeNames propertyNameCreator = new PropertyNameCreator();
		protected INameCreator eventNameCreator = new NameCreator("Event_");
		public INameCreator staticMethodNameCreator = new NameCreator("smethod_");
		public INameCreator virtualMethodNameCreator = new NameCreator("vmethod_");
		public INameCreator instanceMethodNameCreator = new NameCreator("method_");
		protected INameCreator genericPropertyNameCreator = new NameCreator("Prop_");
		Func<string, bool> isValidName;

		public Func<string, bool> IsValidName {
			get { return isValidName; }
			set { isValidName = value; }
		}

		public virtual VariableNameState clone() {
			var rv = new VariableNameState();
			cloneInit(rv);
			return rv;
		}

		public void addFieldName(string fieldName) {
			currentVariableNames.add(fieldName);
		}

		public void addMethodName(string methodName) {
			currentMethodNames.add(methodName);
		}

		protected void cloneInit(VariableNameState variableNameState) {
			variableNameState.currentVariableNames = new CurrentNames();
			variableNameState.currentMethodNames = new CurrentNames();
			variableNameState.variableNameCreator = variableNameCreator.clone();
			variableNameState.propertyNameCreator = propertyNameCreator.clone();
			variableNameState.eventNameCreator = eventNameCreator.clone();
			variableNameState.staticMethodNameCreator = staticMethodNameCreator.clone();
			variableNameState.virtualMethodNameCreator = virtualMethodNameCreator.clone();
			variableNameState.instanceMethodNameCreator = instanceMethodNameCreator.clone();
			variableNameState.genericPropertyNameCreator = genericPropertyNameCreator.clone();
			variableNameState.isValidName = isValidName;
		}

		public string getNewPropertyName(PropertyDefinition propertyDefinition) {
			var propType = propertyDefinition.PropertyType;
			if (propType is GenericParameter)
				return genericPropertyNameCreator.newName();
			return propertyNameCreator.newName(propType);
		}

		public string getNewEventName(EventDefinition eventDefinition) {
			return eventNameCreator.newName();
		}

		public string getNewFieldName(FieldDefinition field) {
			return currentVariableNames.newName(field.Name, () => variableNameCreator.newName(field.FieldType));
		}

		public string getNewFieldName(string oldName, INameCreator nameCreator) {
			return currentVariableNames.newName(oldName, () => nameCreator.newName());
		}

		public string getNewParamName(string oldName, ParameterDefinition param) {
			return currentVariableNames.newName(oldName, () => variableNameCreator.newName(param.ParameterType));
		}

		public string getNewMethodName(string oldName, INameCreator nameCreator) {
			return currentMethodNames.newName(oldName, nameCreator);
		}
	}

	class InterfaceVariableNameState : VariableNameState {
		public InterfaceVariableNameState() {
			propertyNameCreator = new GlobalInterfacePropertyNameCreator();
			eventNameCreator = new GlobalNameCreator(new NameCreator("I_Event_"));
			virtualMethodNameCreator = new GlobalNameCreator(new NameCreator("imethod_"));
			genericPropertyNameCreator = new GlobalNameCreator(new NameCreator("I_Prop_"));
		}

		public override VariableNameState clone() {
			var rv = new InterfaceVariableNameState();
			cloneInit(rv);
			return rv;
		}
	}
}
