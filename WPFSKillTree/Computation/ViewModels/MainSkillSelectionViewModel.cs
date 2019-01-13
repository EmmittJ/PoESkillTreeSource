﻿using System.Collections.ObjectModel;
using PoESkillTree.GameModel.Skills;
using POESKillTree.Utils;

namespace POESKillTree.Computation.ViewModels
{
    public class MainSkillSelectionViewModel : Notifier
    {
        private readonly SkillDefinitions _skillDefinitions;
        private MainSkillViewModel _selectedSkill;
        private uint _skillStage;
        private uint _maximumSkillStage;

        public MainSkillSelectionViewModel(SkillDefinitions skillDefinitions)
            => _skillDefinitions = skillDefinitions;

        public void AddSkill(Skill skill)
        {
            AvailableSkills.Add(new MainSkillViewModel(_skillDefinitions.GetSkillById(skill.Id), skill));
            if (AvailableSkills.Count == 1)
            {
                SelectedSkill = AvailableSkills[0];
            }
        }

        public ObservableCollection<MainSkillViewModel> AvailableSkills { get; } =
            new ObservableCollection<MainSkillViewModel>();

        public MainSkillViewModel SelectedSkill
        {
            get => _selectedSkill;
            set => SetProperty(ref _selectedSkill, value);
        }

        public uint SkillStage
        {
            get => _skillStage;
            set => SetProperty(ref _skillStage, value);
        }

        public uint MaximumSkillStage
        {
            get => _maximumSkillStage;
            set => SetProperty(ref _maximumSkillStage, value);
        }
    }
}