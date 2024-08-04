using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class ComponentUtil
{
	public static string GetPath(this Component component)
	{
		return component.transform.GetPath() + "/" + component.GetType().ToString();
	}
}
