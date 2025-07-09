using System;
using System.Collections.Generic;
using UnityEngine;

public class UnitFormationManager : MonoBehaviour
{
    public static UnitFormationManager Instance { get; private set; }

    [Tooltip("Parent under which all formations will be grouped in the hierarchy")]
    public Transform formationsRoot;

    // Maps each CombatUnit to its formation handler
    private readonly Dictionary<CombatUnit, UnitFormation> formations = new Dictionary<CombatUnit, UnitFormation>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }
    }

    /// <summary>
    /// Call after CombatUnit.Initialize(): builds formation according to its data.
    /// </summary>
    public void RegisterFormation(CombatUnit unit)
    {
        // Create container
        var container = new GameObject($"Formation_{unit.data.unitName}");
        container.transform.SetParent(formationsRoot, worldPositionStays: true);
        container.transform.position = unit.transform.position;

        // Parameters from unit data
        var memberPrefab = unit.data.formationMemberPrefab;
        int size = unit.data.formationSize;
        FormationShape shape = unit.data.formationShape;
        float spacing = unit.data.formationSpacing;

        // Instantiate members in chosen shape
        switch (shape)
        {
            case FormationShape.Square:
                int perRow = Mathf.CeilToInt(Mathf.Sqrt(size));
                for (int i = 0; i < size; i++)
                {
                    int row = i / perRow;
                    int col = i % perRow;
                    var member = Instantiate(memberPrefab, container.transform);
                    member.transform.localPosition = new Vector3(
                        (col - (perRow - 1) / 2f) * spacing,
                        0,
                        (row - (perRow - 1) / 2f) * spacing
                    );
                    member.transform.localRotation = Quaternion.Euler(0, 180f, 0);
                }
                break;

            case FormationShape.Circle:
                for (int i = 0; i < size; i++)
                {
                    float angle = i * Mathf.PI * 2f / size;
                    var member = Instantiate(memberPrefab, container.transform);
                    member.transform.localPosition = new Vector3(
                        Mathf.Cos(angle) * spacing,
                        0,
                        Mathf.Sin(angle) * spacing
                    );
                    member.transform.localRotation = Quaternion.Euler(0, -angle * Mathf.Rad2Deg, 0);
                }
                break;

            case FormationShape.Wedge:
                int rows = Mathf.CeilToInt(Mathf.Sqrt(size));
                int count = 0;
                for (int r = 0; r < rows && count < size; r++)
                {
                    int rowCount = r + 1;
                    float startOffset = - (rowCount - 1) * spacing / 2f;
                    for (int c = 0; c < rowCount && count < size; c++, count++)
                    {
                        var member = Instantiate(memberPrefab, container.transform);
                        member.transform.localPosition = new Vector3(
                            startOffset + c * spacing,
                            0,
                            -r * spacing
                        );
                        member.transform.localRotation = Quaternion.Euler(0, 0, 0);
                    }
                }
                break;
        }

        // Attach formation script
        var form = container.AddComponent<UnitFormation>();
        form.Initialize(unit);
        formations[unit] = form;

        // Hook events
        unit.OnHealthChanged    += form.HandleHealthChanged;
        unit.OnAnimationTrigger += form.BroadcastAnimationTrigger;
        unit.OnDeath            += () => UnregisterFormation(unit);
    }

    /// <summary> Destroy formation when unit dies. </summary>
    public void UnregisterFormation(CombatUnit unit)
    {
        if (!formations.TryGetValue(unit, out var form)) return;
        Destroy(form.gameObject);
        formations.Remove(unit);
    }
} 