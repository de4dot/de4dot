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
	class VariableNameState {
		ExistingNames existingVariableNames = new ExistingNames();
		ExistingNames existingMethodNames = new ExistingNames();
		ExistingNames existingPropertyNames = new ExistingNames();
		ExistingNames existingEventNames = new ExistingNames();
		TypeNames variableNameCreator = new VariableNameCreator();	// For fields and method args
		TypeNames propertyNameCreator = new PropertyNameCreator();
		INameCreator eventNameCreator = new NameCreator("Event_");
		INameCreator genericPropertyNameCreator = new NameCreator("Prop_");
		public INameCreator staticMethodNameCreator = new NameCreator("smethod_");
		public INameCreator instanceMethodNameCreator = new NameCreator("method_");

		public virtual VariableNameState clone() {
			var rv = new VariableNameState();
			cloneInit(rv);
			return rv;
		}

		void cloneInit(VariableNameState variableNameState) {
			variableNameState.existingVariableNames = existingVariableNames.clone();
			variableNameState.existingMethodNames = existingMethodNames.clone();
			variableNameState.existingPropertyNames = existingPropertyNames.clone();
			variableNameState.existingEventNames = existingEventNames.clone();
			variableNameState.variableNameCreator = variableNameCreator.clone();
			variableNameState.propertyNameCreator = propertyNameCreator.clone();
			variableNameState.eventNameCreator = eventNameCreator.clone();
			variableNameState.genericPropertyNameCreator = genericPropertyNameCreator.clone();
			variableNameState.staticMethodNameCreator = staticMethodNameCreator.clone();
			variableNameState.instanceMethodNameCreator = instanceMethodNameCreator.clone();
		}

		public string getNewPropertyName(PropertyDefinition propertyDefinition) {
			var propType = propertyDefinition.PropertyType;
			string newName;
			if (propType is GenericParameter)
				newName = genericPropertyNameCreator.create();
			else
				newName = propertyNameCreator.create(propType);
			addPropertyName(newName);
			return newName;
		}

		public string getNewEventName(EventDefinition eventDefinition) {
			string newName = eventNameCreator.create();
			addEventName(newName);
			return newName;
		}

		public void addFieldName(string fieldName) {
			existingVariableNames.add(fieldName);
		}

		public void addParamName(string paramName) {
			existingVariableNames.add(paramName);
		}

		public void addMethodName(string methodName) {
			existingMethodNames.add(methodName);
		}

		public void addPropertyName(string propName) {
			existingPropertyNames.add(propName);
		}

		public void addEventName(string eventName) {
			existingEventNames.add(eventName);
		}

		public bool isMethodNameUsed(string methodName) {
			return existingMethodNames.exists(methodName);
		}

		public bool isPropertyNameUsed(string propName) {
			return existingPropertyNames.exists(propName);
		}

		public bool isEventNameUsed(string eventName) {
			return existingEventNames.exists(eventName);
		}

		public string getNewFieldName(FieldDefinition field) {
			return existingVariableNames.getName(field.Name, () => variableNameCreator.create(field.FieldType));
		}

		public string getNewFieldName(string oldName, INameCreator nameCreator) {
			return existingVariableNames.getName(oldName, () => nameCreator.create());
		}

		public string getNewParamName(string oldName, ParameterDefinition param) {
			return existingVariableNames.getName(oldName, () => variableNameCreator.create(param.ParameterType));
		}

		public string getNewMethodName(string oldName, INameCreator nameCreator) {
			return existingMethodNames.getName(oldName, nameCreator);
		}
	}
}
