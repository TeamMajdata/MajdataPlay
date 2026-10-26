using MajdataPlay.Collections;
using MajdataPlay.Scenes.Game.Utils;
using MajdataPlay.IO;
using MajdataPlay.Utils;
using System.Collections.Generic;
using UnityEngine;

#nullable enable
namespace MajdataPlay.Scenes.Game.Notes.Behaviours
{
    public class TouchBorder : MonoBehaviour
    {
        public SensorArea AreaPosition { get; set; } = SensorArea.C;

        List<NoteRegister> queue = new(64);

        GameObject _two = null!, _three = null!;
        SpriteRenderer _twoRenderer = null!, _threeRenderer = null!;
        Sprite[] _normal = new Sprite[2];
        Sprite[] _each = new Sprite[2];
        Sprite[] _break = new Sprite[2];

        readonly float USERSETTING_TOUCH_SCALE = MajEnv.Settings?.Display.TouchScale ?? 1;
        void Start()
        {
            var index = AreaPosition.GetIndex();
            var area = AreaPosition.GetGroup() switch
            {
                SensorGroup.A => 'A',
                SensorGroup.B => 'B',
                SensorGroup.D => 'D',
                SensorGroup.E => 'E',
                _ => 'C',
            };
            var pos = NoteHelper.GetTouchAreaPosition(index, area);
            NoteHelper.Sorting.SetPosition(transform, pos);
            var scale = transform.localScale;
            transform.localScale = new Vector3(scale.x * USERSETTING_TOUCH_SCALE, scale.y * USERSETTING_TOUCH_SCALE, 1f);
            _two = transform.GetChild(0).gameObject;
            _three = transform.GetChild(1).gameObject;
            _twoRenderer = _two.GetComponent<SpriteRenderer>();
            _threeRenderer = _three.GetComponent<SpriteRenderer>();
            NoteHelper.Sorting.Apply(_twoRenderer, NoteHelper.Sorting.NoteLayer.Touch);
            NoteHelper.Sorting.Apply(_threeRenderer, NoteHelper.Sorting.NoteLayer.Touch);
            var skin = MajInstances.SkinManager.GetTouchSkin();
            skin.Border_Normal.CopyTo(_normal);
            skin.Border_Each.CopyTo(_each);
            skin.Border_Break.CopyTo(_break);

            _twoRenderer.sprite = _normal[0];
            _threeRenderer.sprite = _normal[1];

            _twoRenderer.enabled = false;
            _threeRenderer.enabled = false;

            _two.SetActive(true);
            _three.SetActive(true);
        }
        public void Add(bool isBreak, bool isEach)
        {
            var register = new NoteRegister()
            {
                IsBreak = isBreak,
                IsEach = isEach
            };
            queue.Add(register);
            Refresh();
        }
        public void Remove()
        {
            if (!queue.IsEmpty())
            {
                queue.RemoveAt(0);
            }
            Refresh();
        }
        internal void Clear()
        {
            queue.Clear();
            Refresh();
        }
        void Refresh()
        {
            if (queue.Count < 2)
            {
                _twoRenderer.enabled = false;
                _threeRenderer.enabled = false;
                return;
            }

            var first = queue[1];
            NoteRegister? second = null;
            if (queue.Count > 2)
            {
                second = queue[2];
            }

            _twoRenderer.enabled = true;
            if (first.IsBreak)
            {
                _twoRenderer.sprite = _break[0];
            }
            else if (first.IsEach)
            {
                _twoRenderer.sprite = _each[0];
            }
            else
            {
                _twoRenderer.sprite = _normal[0];
            }

            if (second is not null)
            {
                var _second = (NoteRegister)second;
                _threeRenderer.enabled = true;
                if (_second.IsBreak)
                {
                    _threeRenderer.sprite = _break[1];
                }
                else if (_second.IsEach)
                {
                    _threeRenderer.sprite = _each[1];
                }
                else
                {
                    _threeRenderer.sprite = _normal[1];
                }
            }
            else
            {
                _threeRenderer.enabled = false;
            }

        }
        struct NoteRegister
        {
            public bool IsBreak { get; set; }
            public bool IsEach { get; set; }
        }
    }
}
