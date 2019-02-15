using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A node in a PointOctree
// Copyright 2014 Nition, BSD licence (see LICENCE file). http://nition.co
namespace UnityOctree.Scripts {
  public class PointOctreeNode<T> {
    // Centre of this node
    public Vector3 Center { get; private set; }

    // Length of the sides of this node
    public float SideLength { get; private set; }

    // Minimum size for a node in this octree
    float _min_size;

    // Bounding box that represents this node
    Bounds _bounds = default(Bounds);

    // Objects in this node
    readonly List<OctreeObject> _objects = new List<OctreeObject>();

    // Child nodes, if any
    PointOctreeNode<T>[] _children = null;

    bool HasChildren { get { return this._children != null; } }

    // bounds of potential children to this node. These are actual size (with looseness taken into account), not base size
    Bounds[] _child_bounds;

    // If there are already NUM_OBJECTS_ALLOWED in a node, we split it into children
    // A generally good number seems to be something around 8-15
    const int _num_objects_allowed = 8;

    // For reverting the bounds size after temporary changes
    Vector3 _actual_bounds_size;

    // An object in the octree
    class OctreeObject {
      public T _Obj;
      public Vector3 _Pos;
    }

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="base_length_val">Length of this node, not taking looseness into account.</param>
    /// <param name="min_size_val">Minimum size of nodes in this octree.</param>
    /// <param name="center_val">Centre position of this node.</param>
    public PointOctreeNode(float base_length_val, float min_size_val, Vector3 center_val) {
      this.SetValues(base_length_val, min_size_val, center_val);
    }

    // #### PUBLIC METHODS ####

    /// <summary>
    /// Add an object.
    /// </summary>
    /// <param name="obj">Object to add.</param>
    /// <param name="obj_pos">Position of the object.</param>
    /// <returns></returns>
    public bool Add(T obj, Vector3 obj_pos) {
      if (!Encapsulates(this._bounds, obj_pos)) {
        return false;
      }

      this.SubAdd(obj, obj_pos);
      return true;
    }

    /// <summary>
    /// Remove an object. Makes the assumption that the object only exists once in the tree.
    /// </summary>
    /// <param name="obj">Object to remove.</param>
    /// <returns>True if the object was removed successfully.</returns>
    public bool Remove(T obj) {
      var removed = false;

      for (var i = 0; i < this._objects.Count; i++) {
        if (this._objects[i]._Obj.Equals(obj)) {
          removed = this._objects.Remove(this._objects[i]);
          break;
        }
      }

      if (!removed && this._children != null) {
        for (var i = 0; i < 8; i++) {
          removed = this._children[i].Remove(obj);
          if (removed) {
            break;
          }
        }
      }

      if (removed && this._children != null) {
        // Check if we should merge nodes now that we've removed an item
        if (this.ShouldMerge()) {
          this.Merge();
        }
      }

      return removed;
    }

    /// <summary>
    /// Removes the specified object at the given position. Makes the assumption that the object only exists once in the tree.
    /// </summary>
    /// <param name="obj">Object to remove.</param>
    /// <param name="obj_pos">Position of the object.</param>
    /// <returns>True if the object was removed successfully.</returns>
    public bool Remove(T obj, Vector3 obj_pos) {
      if (!Encapsulates(this._bounds, obj_pos)) {
        return false;
      }

      return this.SubRemove(obj, obj_pos);
    }

    /// <summary>
    /// Return objects that are within maxDistance of the specified ray.
    /// </summary>
    /// <param name="ray">The ray.</param>
    /// <param name="max_distance">Maximum distance from the ray to consider.</param>
    /// <param name="result">List result.</param>
    /// <returns>Objects within range.</returns>
    public void GetNearby(ref Ray ray, float max_distance, List<T> result) {
      // Does the ray hit this node at all?
      // Note: Expanding the bounds is not exactly the same as a real distance check, but it's fast.
      // TODO: Does someone have a fast AND accurate formula to do this check?
      this._bounds.Expand(new Vector3(max_distance * 2, max_distance * 2, max_distance * 2));
      var intersected = this._bounds.IntersectRay(ray);
      this._bounds.size = this._actual_bounds_size;
      if (!intersected) {
        return;
      }

      // Check against any objects in this node
      for (var i = 0; i < this._objects.Count; i++) {
        if (SqrDistanceToRay(ray, this._objects[i]._Pos) <= (max_distance * max_distance)) {
          result.Add(this._objects[i]._Obj);
        }
      }

      // Check children
      if (this._children != null) {
        for (var i = 0; i < 8; i++) {
          this._children[i].GetNearby(ref ray, max_distance, result);
        }
      }
    }

    /// <summary>
    /// Return objects that are within <paramref name="max_distance"/> of the specified position.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="max_distance">Maximum distance from the position to consider.</param>
    /// <param name="result">List result.</param>
    /// <returns>Objects within range.</returns>
    public void GetNearby(ref Vector3 position, float max_distance, List<T> result) {
      var sqr_max_distance = max_distance * max_distance;

      #if UNITY_2017_1_OR_NEWER
      // Does the node intersect with the sphere of center = position and radius = maxDistance?
      if ((this._bounds.ClosestPoint(position) - position).sqrMagnitude > sqr_max_distance) {
        return;
      }
      #else
		// Does the ray hit this node at all?
		// Note: Expanding the bounds is not exactly the same as a real distance check, but it's fast
		// TODO: Does someone have a fast AND accurate formula to do this check?
		bounds.Expand(new Vector3(maxDistance * 2, maxDistance * 2, maxDistance * 2));
		bool contained = bounds.Contains(position);
		bounds.size = actualBoundsSize;
		if (!contained) {
			return;
		}
      #endif

      // Check against any objects in this node
      for (var i = 0; i < this._objects.Count; i++) {
        if ((position - this._objects[i]._Pos).sqrMagnitude <= sqr_max_distance) {
          result.Add(this._objects[i]._Obj);
        }
      }

      // Check children
      if (this._children != null) {
        for (var i = 0; i < 8; i++) {
          this._children[i].GetNearby(ref position, max_distance, result);
        }
      }
    }

    /// <summary>
    /// Return all objects in the tree.
    /// </summary>
    /// <returns>All objects.</returns>
    public void GetAll(List<T> result) {
      // add directly contained objects
      result.AddRange(this._objects.Select(o => o._Obj));

      // add children objects
      if (this._children != null) {
        for (var i = 0; i < 8; i++) {
          this._children[i].GetAll(result);
        }
      }
    }

    /// <summary>
    /// Set the 8 children of this octree.
    /// </summary>
    /// <param name="child_octrees">The 8 new child nodes.</param>
    public void SetChildren(PointOctreeNode<T>[] child_octrees) {
      if (child_octrees.Length != 8) {
        Debug.LogError("Child octree array must be length 8. Was length: " + child_octrees.Length);
        return;
      }

      this._children = child_octrees;
    }

    /// <summary>
    /// Draws node boundaries visually for debugging.
    /// Must be called from OnDrawGizmos externally. See also: DrawAllObjects.
    /// </summary>
    /// <param name="depth">Used for recurcive calls to this method.</param>
    public void DrawAllBounds(float depth = 0) {
      var tint_val = depth / 7; // Will eventually get values > 1. Color rounds to 1 automatically
      Gizmos.color = new Color(tint_val, 0, 1.0f - tint_val);

      var this_bounds =
          new Bounds(this.Center, new Vector3(this.SideLength, this.SideLength, this.SideLength));
      Gizmos.DrawWireCube(this_bounds.center, this_bounds.size);

      if (this._children != null) {
        depth++;
        for (var i = 0; i < 8; i++) {
          this._children[i].DrawAllBounds(depth);
        }
      }

      Gizmos.color = Color.white;
    }

    /// <summary>
    /// Draws the bounds of all objects in the tree visually for debugging.
    /// Must be called from OnDrawGizmos externally. See also: DrawAllBounds.
    /// NOTE: marker.tif must be placed in your Unity /Assets/Gizmos subfolder for this to work.
    /// </summary>
    public void DrawAllObjects() {
      var tint_val = this.SideLength / 20;
      Gizmos.color = new Color(0, 1.0f - tint_val, tint_val, 0.25f);

      foreach (var obj in this._objects) {
        Gizmos.DrawIcon(obj._Pos, "marker.tif", true);
      }

      if (this._children != null) {
        for (var i = 0; i < 8; i++) {
          this._children[i].DrawAllObjects();
        }
      }

      Gizmos.color = Color.white;
    }

    /// <summary>
    /// We can shrink the octree if:
    /// - This node is >= double minLength in length
    /// - All objects in the root node are within one octant
    /// - This node doesn't have children, or does but 7/8 children are empty
    /// We can also shrink it if there are no objects left at all!
    /// </summary>
    /// <param name="min_length">Minimum dimensions of a node in this octree.</param>
    /// <returns>The new root, or the existing one if we didn't shrink.</returns>
    public PointOctreeNode<T> ShrinkIfPossible(float min_length) {
      if (this.SideLength < (2 * min_length)) {
        return this;
      }

      if (this._objects.Count == 0 && (this._children == null || this._children.Length == 0)) {
        return this;
      }

      // Check objects in root
      var best_fit = -1;
      for (var i = 0; i < this._objects.Count; i++) {
        var cur_obj = this._objects[i];
        var new_best_fit = this.BestFitChild(cur_obj._Pos);
        if (i == 0 || new_best_fit == best_fit) {
          if (best_fit < 0) {
            best_fit = new_best_fit;
          }
        } else {
          return this; // Can't reduce - objects fit in different octants
        }
      }

      // Check objects in children if there are any
      if (this._children != null) {
        var child_had_content = false;
        for (var i = 0; i < this._children.Length; i++) {
          if (this._children[i].HasAnyObjects()) {
            if (child_had_content) {
              return this; // Can't shrink - another child had content already
            }

            if (best_fit >= 0 && best_fit != i) {
              return this; // Can't reduce - objects in root are in a different octant to objects in child
            }

            child_had_content = true;
            best_fit = i;
          }
        }
      }

      // Can reduce
      if (this._children == null) {
        // We don't have any children, so just shrink this node to the new size
        // We already know that everything will still fit in it
        this.SetValues(this.SideLength / 2, this._min_size, this._child_bounds[best_fit].center);
        return this;
      }

      // We have children. Use the appropriate child as the new root node
      return this._children[best_fit];
    }

    /// <summary>
    /// Find which child node this object would be most likely to fit in.
    /// </summary>
    /// <param name="obj_pos">The object's position.</param>
    /// <returns>One of the eight child octants.</returns>
    public int BestFitChild(Vector3 obj_pos) {
      return (obj_pos.x <= this.Center.x ? 0 : 1)
             + (obj_pos.y >= this.Center.y ? 0 : 4)
             + (obj_pos.z <= this.Center.z ? 0 : 2);
    }

    /// <summary>
    /// Checks if this node or anything below it has something in it.
    /// </summary>
    /// <returns>True if this node or any of its children, grandchildren etc have something in them</returns>
    public bool HasAnyObjects() {
      if (this._objects.Count > 0) {
        return true;
      }

      if (this._children != null) {
        for (var i = 0; i < 8; i++) {
          if (this._children[i].HasAnyObjects()) {
            return true;
          }
        }
      }

      return false;
    }

    /*
/// <summary>
/// Get the total amount of objects in this node and all its children, grandchildren etc. Useful for debugging.
/// </summary>
/// <param name="startingNum">Used by recursive calls to add to the previous total.</param>
/// <returns>Total objects in this node and its children, grandchildren etc.</returns>
public int GetTotalObjects(int startingNum = 0) {
  int totalObjects = startingNum + objects.Count;
  if (children != null) {
    for (int i = 0; i < 8; i++) {
      totalObjects += children[i].GetTotalObjects();
    }
  }
  return totalObjects;
}
*/

    // #### PRIVATE METHODS ####

    /// <summary>
    /// Set values for this node. 
    /// </summary>
    /// <param name="base_length_val">Length of this node, not taking looseness into account.</param>
    /// <param name="min_size_val">Minimum size of nodes in this octree.</param>
    /// <param name="center_val">Centre position of this node.</param>
    void SetValues(float base_length_val, float min_size_val, Vector3 center_val) {
      this.SideLength = base_length_val;
      this._min_size = min_size_val;
      this.Center = center_val;

      // Create the bounding box.
      this._actual_bounds_size = new Vector3(this.SideLength, this.SideLength, this.SideLength);
      this._bounds = new Bounds(this.Center, this._actual_bounds_size);

      var quarter = this.SideLength / 4f;
      var child_actual_length = this.SideLength / 2;
      var child_actual_size = new Vector3(child_actual_length, child_actual_length, child_actual_length);
      this._child_bounds = new Bounds[8];
      this._child_bounds[0] =
          new Bounds(this.Center + new Vector3(-quarter, quarter, -quarter), child_actual_size);
      this._child_bounds[1] =
          new Bounds(this.Center + new Vector3(quarter, quarter, -quarter), child_actual_size);
      this._child_bounds[2] =
          new Bounds(this.Center + new Vector3(-quarter, quarter, quarter), child_actual_size);
      this._child_bounds[3] =
          new Bounds(this.Center + new Vector3(quarter, quarter, quarter), child_actual_size);
      this._child_bounds[4] =
          new Bounds(this.Center + new Vector3(-quarter, -quarter, -quarter), child_actual_size);
      this._child_bounds[5] =
          new Bounds(this.Center + new Vector3(quarter, -quarter, -quarter), child_actual_size);
      this._child_bounds[6] =
          new Bounds(this.Center + new Vector3(-quarter, -quarter, quarter), child_actual_size);
      this._child_bounds[7] =
          new Bounds(this.Center + new Vector3(quarter, -quarter, quarter), child_actual_size);
    }

    /// <summary>
    /// Private counterpart to the public Add method.
    /// </summary>
    /// <param name="obj">Object to add.</param>
    /// <param name="obj_pos">Position of the object.</param>
    void SubAdd(T obj, Vector3 obj_pos) {
      // We know it fits at this level if we've got this far

      // We always put things in the deepest possible child
      // So we can skip checks and simply move down if there are children aleady
      if (!this.HasChildren) {
        // Just add if few objects are here, or children would be below min size
        if (this._objects.Count < _num_objects_allowed || (this.SideLength / 2) < this._min_size) {
          var new_obj = new OctreeObject {_Obj = obj, _Pos = obj_pos};
          this._objects.Add(new_obj);
          return; // We're done. No children yet
        }

        // Enough objects in this node already: Create the 8 children
        int best_fit_child;
        if (this._children == null) {
          this.Split();
          if (this._children == null) {
            Debug.LogError("Child creation failed for an unknown reason. Early exit.");
            return;
          }

          // Now that we have the new children, move this node's existing objects into them
          for (var i = this._objects.Count - 1; i >= 0; i--) {
            var existing_obj = this._objects[i];
            // Find which child the object is closest to based on where the
            // object's center is located in relation to the octree's center
            best_fit_child = this.BestFitChild(existing_obj._Pos);
            this._children[best_fit_child]
                .SubAdd(existing_obj._Obj, existing_obj._Pos); // Go a level deeper					
            this._objects.Remove(existing_obj); // Remove from here
          }
        }
      }

      // Handle the new object we're adding now
      var best_fit = this.BestFitChild(obj_pos);
      this._children[best_fit].SubAdd(obj, obj_pos);
    }

    /// <summary>
    /// Private counterpart to the public <see cref="Remove(T, Vector3)"/> method.
    /// </summary>
    /// <param name="obj">Object to remove.</param>
    /// <param name="obj_pos">Position of the object.</param>
    /// <returns>True if the object was removed successfully.</returns>
    bool SubRemove(T obj, Vector3 obj_pos) {
      var removed = false;

      for (var i = 0; i < this._objects.Count; i++) {
        if (this._objects[i]._Obj.Equals(obj)) {
          removed = this._objects.Remove(this._objects[i]);
          break;
        }
      }

      if (!removed && this._children != null) {
        var best_fit_child = this.BestFitChild(obj_pos);
        removed = this._children[best_fit_child].SubRemove(obj, obj_pos);
      }

      if (removed && this._children != null) {
        // Check if we should merge nodes now that we've removed an item
        if (this.ShouldMerge()) {
          this.Merge();
        }
      }

      return removed;
    }

    /// <summary>
    /// Splits the octree into eight children.
    /// </summary>
    void Split() {
      var quarter = this.SideLength / 4f;
      var new_length = this.SideLength / 2;
      this._children = new PointOctreeNode<T>[8];
      this._children[0] = new PointOctreeNode<T>(new_length,
                                                 this._min_size,
                                                 this.Center + new Vector3(-quarter, quarter, -quarter));
      this._children[1] = new PointOctreeNode<T>(new_length,
                                                 this._min_size,
                                                 this.Center + new Vector3(quarter, quarter, -quarter));
      this._children[2] = new PointOctreeNode<T>(new_length,
                                                 this._min_size,
                                                 this.Center + new Vector3(-quarter, quarter, quarter));
      this._children[3] = new PointOctreeNode<T>(new_length,
                                                 this._min_size,
                                                 this.Center + new Vector3(quarter, quarter, quarter));
      this._children[4] = new PointOctreeNode<T>(new_length,
                                                 this._min_size,
                                                 this.Center + new Vector3(-quarter, -quarter, -quarter));
      this._children[5] = new PointOctreeNode<T>(new_length,
                                                 this._min_size,
                                                 this.Center + new Vector3(quarter, -quarter, -quarter));
      this._children[6] = new PointOctreeNode<T>(new_length,
                                                 this._min_size,
                                                 this.Center + new Vector3(-quarter, -quarter, quarter));
      this._children[7] = new PointOctreeNode<T>(new_length,
                                                 this._min_size,
                                                 this.Center + new Vector3(quarter, -quarter, quarter));
    }

    /// <summary>
    /// Merge all children into this node - the opposite of Split.
    /// Note: We only have to check one level down since a merge will never happen if the children already have children,
    /// since THAT won't happen unless there are already too many objects to merge.
    /// </summary>
    void Merge() {
      // Note: We know children != null or we wouldn't be merging
      for (var i = 0; i < 8; i++) {
        var cur_child = this._children[i];
        var num_objects = cur_child._objects.Count;
        for (var j = num_objects - 1; j >= 0; j--) {
          var cur_obj = cur_child._objects[j];
          this._objects.Add(cur_obj);
        }
      }

      // Remove the child nodes (and the objects in them - they've been added elsewhere now)
      this._children = null;
    }

    /// <summary>
    /// Checks if outerBounds encapsulates the given point.
    /// </summary>
    /// <param name="outer_bounds">Outer bounds.</param>
    /// <param name="point">Point.</param>
    /// <returns>True if innerBounds is fully encapsulated by outerBounds.</returns>
    static bool Encapsulates(Bounds outer_bounds, Vector3 point) { return outer_bounds.Contains(point); }

    /// <summary>
    /// Checks if there are few enough objects in this node and its children that the children should all be merged into this.
    /// </summary>
    /// <returns>True there are less or the same abount of objects in this and its children than numObjectsAllowed.</returns>
    bool ShouldMerge() {
      var total_objects = this._objects.Count;
      if (this._children != null) {
        foreach (var child in this._children) {
          if (child._children != null) {
            // If any of the *children* have children, there are definitely too many to merge,
            // or the child woudl have been merged already
            return false;
          }

          total_objects += child._objects.Count;
        }
      }

      return total_objects <= _num_objects_allowed;
    }

    /// <summary>
    /// Returns the closest distance to the given ray from a point.
    /// </summary>
    /// <param name="ray">The ray.</param>
    /// <param name="point">The point to check distance from the ray.</param>
    /// <returns>Squared distance from the point to the closest point of the ray.</returns>
    public static float SqrDistanceToRay(Ray ray, Vector3 point) {
      return Vector3.Cross(ray.direction, point - ray.origin).sqrMagnitude;
    }
  }
}
