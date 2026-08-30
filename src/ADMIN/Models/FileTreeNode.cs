using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ADMIN.Models
{
    public partial class FileTreeNode : ObservableObject
    {
        [ObservableProperty] private bool _isExpanded;
        [ObservableProperty] private bool _isSelected;

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public bool IsLoaded { get; set; }
        public bool IsLocal { get; }

        public ObservableCollection<FileTreeNode> Children { get; } = new();

        public string Icon => IsDirectory ? "📁" : "📄";

        public FileTreeNode(string fullPath, bool isDirectory, bool isLocal)
        {
            FullPath = fullPath;
            IsDirectory = isDirectory;
            IsLocal = isLocal;
            Name = System.IO.Path.GetFileName(fullPath.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(Name)) Name = fullPath; // root drive e.g. "C:\"

            // Add a dummy child so the expand arrow appears for directories
            if (isDirectory)
                Children.Add(new FileTreeNode("Loading...", false, isLocal) { _isDummy = true });
        }

        private bool _isDummy;
        public bool IsDummy => _isDummy;
    }
}
