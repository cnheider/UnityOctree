using System.Collections.Generic;
using UnityEngine;

// A Dynamic Octree for storing any objects that can be described as a single point
// See also: BoundsOctree, where objects are described by AABB bounds
// Octree:	An octree is a tree data structure which divides 3D space into smaller partitions (nodes)
//			and places objects into the appropriate nodes. This allows fast access to objects
//			in an area of interest without having to check every object.
// Dynamic: The octree grows or shrinks as required when objects as added or removed
//			It also splits and merges nodes as appropriate. There is no maximum depth.
//			Nodes have a constant - numObjectsAllowed - which sets the amount of items allowed in a node before it splits.
// T:		The content of the octree can be anything, since the bounds data is supplied separately.

// Originally written for my game Scraps (http://www.scrapsgame.com) but intended to be general-purpose.
// Copyright 2014 Nition, BSD licence (see LICENCE file). http://nition.co
// Unity-based, but could be adapted to work in pure C#
namespace UnityOctree.Scripts {
  public class PointOctree<T> {
    // The total amount of objects currently in the tree
    public int Count { get; private set; }

    // Root node of the octree
    PointOctreeNode<T> _root_node;

    // Size that the octree was on creation
    readonly float _initial_size;

    // Minimum side length that a node can be - essentially an alternative to having a max depth
    readonly float _min_size;

    /// <summary>
    /// Constructor for the point octree.
    /// </summary>
    /// <param name="initial_world_size">Size of the sides of the initial node. The octree will never shrink smaller than this.</param>
    /// <param name="initial_world_pos">Position of the centre of the initial node.</param>
    /// <param name="min_node_size">Nodes will stop splitting if the new nodes would be smaller than this.</param>
    public PointOctree(float initial_world_size, Vector3 initial_world_pos, float min_node_size) {
      if (min_node_size > initial_world_size) {
        Debug.LogWarning("Minimum node size must be at least as big as the initial world size. Was: "
                         + min_node_size
                         + " Adjusted to: "
                         + initial_world_size);
        min_node_size = initial_world_size;
      }

      this.Count = 0;
      this._initial_size = initial_world_size;
      this._min_size = min_node_size;
      this._root_node = new PointOctreeNode<T>(this._initial_size, this._min_size, initial_world_pos);
    }

    // #### PUBLIC METHODS ####

    /// <summary>
    /// Add an object.
    /// </summary>
    /// <param name="obj">Object to add.</param>
    /// <param name="obj_pos">Position of the object.</param>
    public void Add(T obj, Vector3 obj_pos) {
      // Add object or expand the octree until it can be added
      var count = 0; // Safety check against infinite/excessive growth
      while (!this._root_node.Add(obj, obj_pos)) {
        this.Grow(obj_pos - this._root_node.Center);
        if (++count > 20) {
          Debug.LogError("Aborted Add operation as it seemed to be going on forever ("
                         + (count - 1)
                         + ") attempts at growing the octree.");
          return;
        }
      }

      this.Count++;
    }

    /// <summary>
    /// Remove an object. Makes the assumption that the object only exists once in the tree.
    /// </summary>
    /// <param name="obj">Object to remove.</param>
    /// <returns>True if the object was removed successfully.</returns>
    public bool Remove(T obj) {
      var removed = this._root_node.Remove(obj);

      // See if we can shrink the octree down now that we've removed the item
      if (removed) {
        this.Count--;
        this.Shrink();
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
      var removed = this._root_node.Remove(obj, obj_pos);

      // See if we can shrink the octree down now that we've removed the item
      if (removed) {
        this.Count--;
        this.Shrink();
      }

      return removed;
    }

    /// <summary>
    /// Returns objects that are within <paramref name="max_distance"/> of the specified ray.
    /// If none, returns false. Uses supplied list for results.
    /// </summary>
    /// <param name="ray">The ray. Passing as ref to improve performance since it won't have to be copied.</param>
    /// <param name="max_distance">Maximum distance from the ray to consider</param>
    /// <param name="near_by">Pre-initialized list to populate</param>
    /// <returns>True if items are found, false if not</returns>
    public bool GetNearbyNonAlloc(Ray ray, float max_distance, List<T> near_by) {
      near_by.Clear();
      this._root_node.GetNearby(ref ray, max_distance, near_by);
      if (near_by.Count > 0) {
        return true;
      }

      return false;
    }

    /// <summary>
    /// Returns objects that are within <paramref name="max_distance"/> of the specified ray.
    /// If none, returns an empty array (not null).
    /// </summary>
    /// <param name="ray">The ray. Passing as ref to improve performance since it won't have to be copied.</param>
    /// <param name="max_distance">Maximum distance from the ray to consider.</param>
    /// <returns>Objects within range.</returns>
    public T[] GetNearby(Ray ray, float max_distance) {
      var colliding_with = new List<T>();
      this._root_node.GetNearby(ref ray, max_distance, colliding_with);
      return colliding_with.ToArray();
    }

    /// <summary>
    /// Returns objects that are within <paramref name="max_distance"/> of the specified position.
    /// If none, returns an empty array (not null).
    /// </summary>
    /// <param name="position">The position. Passing as ref to improve performance since it won't have to be copied.</param>
    /// <param name="max_distance">Maximum distance from the position to consider.</param>
    /// <returns>Objects within range.</returns>
    public T[] GetNearby(Vector3 position, float max_distance) {
      var colliding_with = new List<T>();
      this._root_node.GetNearby(ref position, max_distance, colliding_with);
      return colliding_with.ToArray();
    }

    /// <summary>
    /// Returns objects that are within <paramref name="max_distance"/> of the specified position.
    /// If none, returns false. Uses supplied list for results.
    /// </summary>
    /// <param name="max_distance">Maximum distance from the position to consider</param>
    /// <param name="near_by">Pre-initialized list to populate</param>
    /// <returns>True if items are found, false if not</returns>
    public bool GetNearbyNonAlloc(Vector3 position, float max_distance, List<T> near_by) {
      near_by.Clear();
      this._root_node.GetNearby(ref position, max_distance, near_by);
      if (near_by.Count > 0) {
        return true;
      }

      return false;
    }

    /// <summary>
    /// Return all objects in the tree.
    /// If none, returns an empty array (not null).
    /// </summary>
    /// <returns>All objects.</returns>
    public ICollection<T> GetAll() {
      var objects = new List<T>(this.Count);
      this._root_node.GetAll(objects);
      return objects;
    }

    /// <summary>
    /// Draws node boundaries visually for debugging.
    /// Must be called from OnDrawGizmos externally. See also: DrawAllObjects.
    /// </summary>
    public void DrawAllBounds() { this._root_node.DrawAllBounds(); }

    /// <summary>
    /// Draws the bounds of all objects in the tree visually for debugging.
    /// Must be called from OnDrawGizmos externally. See also: DrawAllBounds.
    /// </summary>
    public void DrawAllObjects() { this._root_node.DrawAllObjects(); }

    // #### PRIVATE METHODS ####

    /// <summary>
    /// Grow the octree to fit in all objects.
    /// </summary>
    /// <param name="direction">Direction to grow.</param>
    void Grow(Vector3 direction) {
      var x_direction = direction.x >= 0 ? 1 : -1;
      var y_direction = direction.y >= 0 ? 1 : -1;
      var z_direction = direction.z >= 0 ? 1 : -1;
      var old_root = this._root_node;
      var half = this._root_node.SideLength / 2;
      var new_length = this._root_node.SideLength * 2;
      var new_center = this._root_node.Center
                       + new Vector3(x_direction * half, y_direction * half, z_direction * half);

      // Create a new, bigger octree root node
      this._root_node = new PointOctreeNode<T>(new_length, this._min_size, new_center);

      if (old_root.HasAnyObjects()) {
        // Create 7 new octree children to go with the old root as children of the new root
        var root_pos = this._root_node.BestFitChild(old_root.Center);
        var children = new PointOctreeNode<T>[8];
        for (var i = 0; i < 8; i++) {
          if (i == root_pos) {
            children[i] = old_root;
          } else {
            x_direction = i % 2 == 0 ? -1 : 1;
            y_direction = i > 3 ? -1 : 1;
            z_direction = (i < 2 || (i > 3 && i < 6)) ? -1 : 1;
            children[i] = new PointOctreeNode<T>(old_root.SideLength,
                                                 this._min_size,
                                                 new_center
                                                 + new Vector3(x_direction * half,
                                                               y_direction * half,
                                                               z_direction * half));
          }
        }

        // Attach the new children to the new root node
        this._root_node.SetChildren(children);
      }
    }

    /// <summary>
    /// Shrink the octree if possible, else leave it the same.
    /// </summary>
    void Shrink() { this._root_node = this._root_node.ShrinkIfPossible(this._initial_size); }
  }
}
