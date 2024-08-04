using UnityEngine;

public static class TransformUtil
{
	public static string GetPath(this Transform current)
	{
		if (current.parent == null)
			return current.gameObject.scene.name + "/" + current.name;
		return current.parent.GetPath() + "/" + current.name;
	}
}
