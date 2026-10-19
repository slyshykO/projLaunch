use anyhow::{Context, Result, ensure};
use git_protocol::{Status, StatusEntry};

pub fn parse(bytes: &[u8]) -> Result<Status> {
    let text =
        std::str::from_utf8(bytes).context("Git paths must be valid UTF-8 in protocol v1")?;
    let mut records = text.split('\0').filter(|s| !s.is_empty());
    let mut result = Status::default();
    while let Some(record) = records.next() {
        if let Some(header) = record.strip_prefix("# ") {
            let (name, value) = header.split_once(' ').context("Invalid status header")?;
            match name {
                "branch.oid" if value != "(initial)" => result.head = Some(value.into()),
                "branch.head" if value != "(detached)" => result.branch = Some(value.into()),
                "branch.upstream" => result.upstream = Some(value.into()),
                "branch.ab" => {
                    let (ahead, behind) = value.split_once(' ').context("Invalid branch counts")?;
                    result.ahead = ahead.trim_start_matches('+').parse()?;
                    result.behind = behind.trim_start_matches('-').parse()?;
                }
                _ => {}
            }
            continue;
        }
        let tag = record.as_bytes()[0];
        if tag == b'?' || tag == b'!' {
            result.entries.push(StatusEntry {
                path: record.get(2..).context("Invalid untracked record")?.into(),
                original_path: None,
                index: ".".into(),
                worktree: if tag == b'?' { "?" } else { "!" }.into(),
                kind: if tag == b'?' { "untracked" } else { "ignored" }.into(),
                submodule: "N...".into(),
            });
            continue;
        }
        let count = match tag {
            b'1' => 9,
            b'2' => 10,
            b'u' => 11,
            _ => anyhow::bail!("Unknown status record"),
        };
        let fields: Vec<_> = record.splitn(count, ' ').collect();
        ensure!(
            fields.len() == count && fields[1].len() == 2 && fields[1].is_ascii(),
            "Invalid status fields"
        );
        result.entries.push(StatusEntry {
            path: fields[count - 1].into(),
            original_path: if tag == b'2' {
                Some(records.next().context("Missing rename source")?.into())
            } else {
                None
            },
            index: fields[1][..1].into(),
            worktree: fields[1][1..].into(),
            kind: match tag {
                b'2' => "renamed",
                b'u' => "conflict",
                _ => "tracked",
            }
            .into(),
            submodule: fields[2].into(),
        });
    }
    Ok(result)
}

#[cfg(test)]
mod tests {
    #[test]
    fn nul_framing_preserves_newlines_tabs_and_rename_sources() {
        let bytes = b"# branch.oid (initial)\0# branch.head main\0# branch.ab +2 -3\0\
            1 MM N... 100644 100644 100644 abc def file\nwith\ttab\0\
            2 R. N... 100644 100644 100644 abc def R100 new\nname\0old\tname\0\
            ? --new [literal]\0";
        let status = super::parse(bytes).unwrap();
        assert_eq!(status.entries[0].path, "file\nwith\ttab");
        assert_eq!(status.entries[1].path, "new\nname");
        assert_eq!(
            status.entries[1].original_path.as_deref(),
            Some("old\tname")
        );
        assert_eq!(status.ahead, 2);
        assert_eq!(status.behind, 3);
    }
}
